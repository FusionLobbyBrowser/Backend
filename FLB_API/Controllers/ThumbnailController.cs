using System.Diagnostics.CodeAnalysis;

using FLB_API.Managers;

using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Net.Http.Headers;

namespace FLB_API.Controllers
{
    [ApiController]
    [Route("[controller]/{modId}")]
    public class ThumbnailController : ControllerBase
    {
        [HttpGet(Name = "GetThumbnail")]
        [Produces("image/png")]
        public async Task<IActionResult> Get([FromRoute(Name = "modId")] string modId, [FromQuery(Name = "barcode")] string barcode = "")
        {
            if (string.IsNullOrWhiteSpace(modId) && string.IsNullOrWhiteSpace(barcode))
                return Program.CreateResult("modId is required.", 400);

            if (!long.TryParse(modId, out long _modId) && string.IsNullOrWhiteSpace(barcode))
                return Program.CreateResult("modId is not valid.", 400);

            MemoryThumbnail? thumbnail;
            try
            {
                thumbnail = await ModIOManager.GetModThumbnail(_modId, barcode);
                if (thumbnail == null)
                    return Program.CreateResult("Thumbnail not found.", 404);
            }
            catch (Exception ex)
            {
                Program.Logger?.Error(ex, $"Error fetching thumbnail for {modId}");
                return Program.CreateResult("An error occurred while fetching the thumbnail", 500);
            }

            Response.Headers.AccessControlExposeHeaders = new Microsoft.Extensions.Primitives.StringValues(["ModIO-Maturity", "Server-Uptime"]);
            Response.Headers.Append("Server-Uptime", ((DateTimeOffset)Program.Uptime).ToUnixTimeSeconds().ToString() ?? "-1");
            Response.Headers.Append("ModIO-Maturity", thumbnail.IsNSFW ? "nsfw" : "safe");

            thumbnail.Image.Position = 0;
            return new FLB_API.Controllers.FileStreamResult(thumbnail.Image, "image/png")
            {
                FileDownloadName = $"thumbnail_{thumbnail.ModId}.png",
            };
        }
    }

    public class FileStreamResult : FileResult
    {
        private Stream _fileStream;

        public FileStreamResult(Stream fileStream, string contentType)
            : this(fileStream, MediaTypeHeaderValue.Parse(contentType))
        {
        }

        public FileStreamResult(Stream fileStream, MediaTypeHeaderValue contentType)
            : base(contentType.ToString())
        {
            ArgumentNullException.ThrowIfNull(fileStream);

            FileStream = fileStream;
        }

        public Stream FileStream
        {
            get => _fileStream;

            [MemberNotNull(nameof(_fileStream))]
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                _fileStream = value;
            }
        }

        public override Task ExecuteResultAsync(ActionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            var executor = context.HttpContext.RequestServices.GetRequiredService<IActionResultExecutor<FileStreamResult>>();
            return executor.ExecuteAsync(context, this);
        }
    }

    public partial class FileStreamResultExecutor(ILoggerFactory loggerFactory) : FileResultExecutorBase(CreateLogger<FileStreamResultExecutor>(loggerFactory)), IActionResultExecutor<FileStreamResult>
    {
        public virtual async Task ExecuteAsync(ActionContext context, FileStreamResult result)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(result);

            Log.ExecutingFileResult(Logger, result);

            long? fileLength = null;
            if (result.FileStream.CanSeek)
            {
                fileLength = result.FileStream.Length;
            }

            var (range, rangeLength, serveBody) = SetHeadersAndLog(
                context,
                result,
                fileLength,
                result.EnableRangeProcessing,
                result.LastModified,
                result.EntityTag);

            if (!serveBody)
            {
                return;
            }

            await WriteFileAsync(context, result, range, rangeLength);
        }

        protected virtual Task WriteFileAsync(
            ActionContext context,
            FileStreamResult result,
            RangeItemHeaderValue? range,
            long rangeLength)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(result);

            if (range != null && rangeLength == 0)
                return Task.CompletedTask;

            if (range != null)
                Log.WritingRangeToBody(Logger);

            return LocalWriteFileAsync(context.HttpContext, result.FileStream, range, rangeLength);
        }

        private async Task LocalWriteFileAsync(HttpContext context, Stream fileStream, RangeItemHeaderValue? range, long rangeLength)
        {
            Stream body = context.Response.Body;

            _ = 1;
            try
            {
                if (range == null)
                {
                    await StreamCopyOperation.CopyToAsync(fileStream, body, null, 65536, context.RequestAborted);
                    return;
                }

                fileStream.Seek(range.From.Value, SeekOrigin.Begin);
                await StreamCopyOperation.CopyToAsync(fileStream, body, rangeLength, 65536, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                context.Abort();
            }
        }

        private static partial class Log
        {
            public static void ExecutingFileResult(ILogger logger, FileResult fileResult)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    var fileResultType = fileResult.GetType().Name;
                    ExecutingFileResultWithNoFileName(logger, fileResultType, fileResult.FileDownloadName);
                }
            }

            [LoggerMessage(1, LogLevel.Information, "Executing {FileResultType}, sending file with download name '{FileDownloadName}' ...", EventName = "ExecutingFileResultWithNoFileName", SkipEnabledCheck = true)]
            private static partial void ExecutingFileResultWithNoFileName(ILogger logger, string fileResultType, string fileDownloadName);

            [LoggerMessage(17, LogLevel.Debug, "Writing the requested range of bytes to the body...", EventName = "WritingRangeToBody")]
            public static partial void WritingRangeToBody(ILogger logger);
        }
    }
}