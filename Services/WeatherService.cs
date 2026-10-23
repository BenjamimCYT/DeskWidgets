using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace DeskWidgets.Services
{
    public record WeatherNow(double Temperature, int Code, bool IsDay);

    public record GeoResult(string Name, string Admin1, string Country, double Latitude, double Longitude)
    {
        public string Display
        {
            get
            {
                var parts = new List<string> { Name };
                if (!string.IsNullOrWhiteSpace(Admin1)) parts.Add(Admin1);
                if (!string.IsNullOrWhiteSpace(Country)) parts.Add(Country);
                return string.Join(", ", parts);
            }
        }
    }

    /// <summary>Open-Meteo: gratuito, sem chave de API. Uma requisição pequena a cada 30 min.</summary>
    public static class WeatherService
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("DeskWidgets/1.0");
            return c;
        }

        public static async Task<WeatherNow> GetCurrentAsync(double lat, double lon, string unit)
        {
            string u = unit == "fahrenheit" ? "fahrenheit" : "celsius";
            string url = string.Format(CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,weather_code,is_day&temperature_unit={2}&timezone=auto",
                lat, lon, u);

            using var stream = await Http.GetStreamAsync(url).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            var cur = doc.RootElement.GetProperty("current");
            return new WeatherNow(
                cur.GetProperty("temperature_2m").GetDouble(),
                cur.GetProperty("weather_code").GetInt32(),
                cur.GetProperty("is_day").GetInt32() == 1);
        }

        public static async Task<List<GeoResult>> SearchCityAsync(string name)
        {
            string url = "https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(name)
                         + "&count=8&language=pt&format=json";
            var list = new List<GeoResult>();

            using var stream = await Http.GetStreamAsync(url);
            using var doc = await JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("results", out var arr)) return list;

            foreach (var el in arr.EnumerateArray())
            {
                list.Add(new GeoResult(
                    Str(el, "name"),
                    Str(el, "admin1"),
                    Str(el, "country"),
                    el.GetProperty("latitude").GetDouble(),
                    el.GetProperty("longitude").GetDouble()));
            }
            return list;
        }

        private static string Str(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";

        /// <summary>Códigos WMO → descrição em português.</summary>
        public static string Describe(int code) => code switch
        {
            0 => "Céu limpo",
            1 => "Predominantemente limpo",
            2 => "Parcialmente nublado",
            3 => "Nublado",
            45 or 48 => "Neblina",
            51 or 53 or 55 => "Garoa",
            56 or 57 => "Garoa congelante",
            61 => "Chuva fraca",
            63 => "Chuva",
            65 => "Chuva forte",
            66 or 67 => "Chuva congelante",
            71 or 73 or 75 or 77 => "Neve",
            80 or 81 => "Pancadas de chuva",
            82 => "Pancadas fortes de chuva",
            85 or 86 => "Pancadas de neve",
            95 => "Trovoadas",
            96 or 99 => "Trovoadas com granizo",
            _ => "Tempo indefinido"
        };
    }
}
