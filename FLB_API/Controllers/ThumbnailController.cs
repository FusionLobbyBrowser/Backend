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
        private static readonly Dictionary<string, string> _vanilla = new()
        {
            // Avatars
            ["fa534c5a83ee4ec6bd641fec424c4142.Avatar.Heavy"] = "Heavy",
            ["fa534c5a83ee4ec6bd641fec424c4142.Avatar.Fast"] = "Fast",
            ["fa534c5a83ee4ec6bd641fec424c4142.Avatar.CharFurv4GB"] = "Short",
            ["fa534c5a83ee4ec6bd641fec424c4142.Avatar.CharTallv4"] = "Tall",
            ["fa534c5a83ee4ec6bd641fec424c4142.Avatar.Strong"] = "Strong",
            ["SLZ.BONELAB.Content.Avatar.Anime"] = "Light",
            ["SLZ.BONELAB.Content.Avatar.CharJimmy"] = "Jay",
            ["SLZ.BONELAB.Content.Avatar.FordBW"] = "Ford",
            ["SLZ.BONELAB.Content.Avatar.CharFord"] = "Ford",
            ["SLZ.BONELAB.Core.Avatar.PeasantFemaleA"] = "Peasant",
            ["c3534c5a-10bf-48e9-beca-4ca850656173"] = "Peasant",
            ["c3534c5a-2236-4ce5-9385-34a850656173"] = "Peasant",
            ["c3534c5a-87a3-48b2-87cd-f0a850656173"] = "Peasant",
            ["c3534c5a-f12c-44ef-b953-b8a850656173"] = "Peasant",
            ["c3534c5a-3763-4ddf-bd86-6ca850656173"] = "Peasant",
            ["SLZ.BONELAB.Content.Avatar.Nullbody"] = "Nullbody",
            ["fa534c5a83ee4ec6bd641fec424c4142.Avatar.Charskeleton"] = "Skeleton",
            ["c3534c5a-d388-4945-b4ff-9c7a53656375"] = "Security Guard",
            ["c3534c5a-94b2-40a4-912a-24a8506f6c79"] = "PolyBlank",

            // Maps
            ["c2534c5a-80e1-4a29-93ca-f3254d656e75"] = "Main Menu",
            ["c2534c5a-4197-4879-8cd3-4a695363656e"] = "Descent",
            ["c2534c5a-6b79-40ec-8e98-e58c5363656e"] = "BONELAB Hub",
            ["c2534c5a-56a6-40ab-a8ce-23074c657665"] = "LongRun",
            ["c2534c5a-54df-470b-baaf-741f4c657665"] = "Mine Dive",
            ["c2534c5a-7601-4443-bdfe-7f235363656e"] = "Big Anomaly",
            ["SLZ.BONELAB.Content.Level.LevelStreetPunch"] = "Street Puncher",
            ["SLZ.BONELAB.Content.Level.SprintBridge04"] = "Sprint Bridge",
            ["SLZ.BONELAB.Content.Level.SceneMagmaGate"] = "Magma Gate",
            ["SLZ.BONELAB.Content.Level.MoonBase"] = "Moon Base",
            ["SLZ.BONELAB.Content.Level.LevelKartRace"] = "Monogon Motorway",
            ["c2534c5a-c056-4883-ac79-e051426f6964"] = "Pillar Climb",
            ["SLZ.BONELAB.Content.Level.LevelBigAnomalyB"] = "Big Anomaly B",
            ["c2534c5a-db71-49cf-b694-24584c657665"] = "Ascent",
            ["fa534c5a868247138f50c62e424c4144.Level.VoidG114"] = "VoidG114",
            ["c2534c5a-61b3-4f97-9059-79155363656e"] = "Baseline",
            ["c2534c5a-2c4c-4b44-b076-203b5363656e"] = "Tuscany",
            ["fa534c5a83ee4ec6bd641fec424c4142.Level.LevelMuseumBasement"] = "Museum Basement",
            ["fa534c5a83ee4ec6bd641fec424c4142.Level.LevelHalfwayPark"] = "Halfway Park",
            ["fa534c5a83ee4ec6bd641fec424c4142.Level.LevelGunRange"] = "Gun Range",
            ["fa534c5a83ee4ec6bd641fec424c4142.Level.LevelHoloChamber"] = "HoloChamber",
            ["fa534c5a83ee4ec6bd641fec424c4142.Level.LevelKartBowling"] = "Big Bone Bowling",
            ["SLZ.BONELAB.Content.Level.LevelMirror"] = "Mirror",
            ["c2534c5a-4f3b-480e-ad2f-69175363656e"] = "Neon District Tac Trial",
            ["c2534c5a-de61-4df9-8f6c-416954726547"] = "Drop Pit",
            ["c2534c5a-c180-40e0-b2b7-325c5363656e"] = "Tunnel Tipper",
            ["fa534c5a868247138f50c62e424c4144.Level.LevelArenaMin"] = "Fantasy Arena",
            ["c2534c5a-162f-4661-a04d-975d5363656e"] = "Container Yard",
            ["c2534c5a-5c2f-4eef-a851-66214c657665"] = "Dungeon Warrior",
            ["c2534c5a-c6ac-48b4-9c5f-b5cd5363656e"] = "Rooftops",
            ["fa534c5a83ee4ec6bd641fec424c4142.Level.SceneparkourDistrictLogic"] = "Neon District Parkour",
        };

        public static IReadOnlyDictionary<string, string> Vanilla => _vanilla.AsReadOnly();

        [HttpGet(Name = "GetThumbnail")]
        [Produces("image/png")]
        public async Task<IActionResult> Get([FromRoute(Name = "modId")] string modId, [FromQuery(Name = "barcode")] string barcode = "")
        {
            if (string.IsNullOrWhiteSpace(modId) && string.IsNullOrWhiteSpace(barcode))
                return Program.CreateResult("modId is required.", 400);

            if (!long.TryParse(modId, out long _modId) && string.IsNullOrWhiteSpace(barcode))
                return Program.CreateResult("modId is not valid.", 400);

            MemoryThumbnail? thumbnail;
            if (_modId != -1 || string.IsNullOrWhiteSpace(barcode) || !Vanilla.TryGetValue(barcode, out string? fileName))
            {
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
            }
            else
            {
                var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Vanilla", $"{fileName}.webp");
                if (!System.IO.File.Exists(file))
                {
                    Program.Logger?.Error($"The directory/file for the vanilla thumbnail was not found! Barcode: {barcode} ... File Name: {fileName}.webp");
                    return Program.CreateResult("Thumbnail not found. (Vanilla thumbnail missing)", 404);
                }

                thumbnail = new(-1, await System.IO.File.ReadAllBytesAsync(file), null);
            }

            Response.Headers.AccessControlExposeHeaders = new Microsoft.Extensions.Primitives.StringValues(["ModIO-Maturity", "Server-Uptime"]);
            Response.Headers.Append("Server-Uptime", ((DateTimeOffset)Program.Uptime).ToUnixTimeSeconds().ToString() ?? "-1");
            Response.Headers.Append("ModIO-Maturity", thumbnail.IsNSFW ? "nsfw" : "safe");

            var stream = new MemoryStream(thumbnail.Image, false)
            {
                Position = 0,
            };
            return File(stream, "image/png", $"thumbnail_{(thumbnail.ModId != -1 ? thumbnail.ModId : barcode)}.png");
        }
    }
}