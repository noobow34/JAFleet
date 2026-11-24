using System.Security.Cryptography;
using System.Text;
using JAFleet.Commons.Data;
using JAFleet.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JAFleet.Controllers
{
    /// <summary>
    /// 機体詳細の OGP 用カード画像（/OgImage/{レジ}.png）。
    /// SNS や LINE がリンクのプレビューを作るときに取りに来る。中身が変わらないあいだは ETag で 304 を返す。
    /// </summary>
    public class OgImageController(JAFleetContext context, OgCardRenderer renderer) : Controller
    {
        public const string SITE_URL = "https://ja-fleet.noobow.me";

        /// <summary>機体詳細の og:image に入れる URL</summary>
        public static string ImageUrl(string registrationNumber)
            => $"{SITE_URL}/OgImage/{Uri.EscapeDataString(registrationNumber)}.png";

        public IActionResult Index(string id)
        {
            string reg = (id ?? string.Empty).Trim().ToUpperInvariant();
            var av = context.AircraftViews.AsNoTracking().FirstOrDefault(a => a.RegistrationNumber == reg);
            if (av == null)
            {
                return NotFound();
            }
            var data = ToCardData(av);
            string etag = $"\"{Signature(data)}\"";
            Response.Headers.CacheControl = "public, max-age=86400";
            Response.Headers.ETag = etag;
            if (Request.Headers.IfNoneMatch.ToString() == etag)
            {
                return StatusCode(StatusCodes.Status304NotModified);
            }
            return File(renderer.Render(data), "image/png");
        }

        public static OgCardData ToCardData(AircraftView av) => new(
            av.RegistrationNumber ?? string.Empty,
            av.TypeDetailName ?? av.TypeName,
            av.AirlineNameJpShort,
            av.Operation,
            av.OperationCode,
            av.SpecialLivery);

        /// <summary>画像の中身が変わったら変わる値（ETag 用）。描き方を変えたときは先頭の版を上げる</summary>
        public static string Signature(OgCardData d)
        {
            string text = string.Join("\u001f", "v1", d.Registration, d.Type, d.Airline, d.Operation, d.OperationCode, d.SpecialLivery);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
        }
    }
}
