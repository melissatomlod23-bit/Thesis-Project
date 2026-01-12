using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Data.SqlClient;

// ---------- CONFIG ----------
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5000"); // expose to LAN
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

string connectionString = "Server=192.168.101.100,1433;Database=power_Db;User Id=sa;Password=Xdkeith1234;TrustServerCertificate=True;";
const double DefaultRatePerKWh = 13.0851;

// ---------- HELPERS ----------
async Task<List<(double Watt, DateTime Timestamp)>> GetReadingsOrderedAsync(int appliance)
{
    var list = new List<(double Watt, DateTime Timestamp)>();
    try
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        if (appliance == 1 || appliance == 2)
        {
            string query = appliance == 1
                ? "SELECT Watt, Timestamp FROM SensorData ORDER BY Timestamp ASC;"
                : "SELECT Watt, Timestamp FROM SensorData2 ORDER BY Timestamp ASC;";

            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add((Convert.ToDouble(reader.GetValue(0)), reader.GetDateTime(1)));
        }
        else
        {
            // BOTH
            var readings1 = new List<(double Watt, DateTime Timestamp)>();
            var readings2 = new List<(double Watt, DateTime Timestamp)>();

            using (var cmd1 = new SqlCommand("SELECT Watt, Timestamp FROM SensorData ORDER BY Timestamp ASC;", conn))
            using (var reader1 = await cmd1.ExecuteReaderAsync())
                while (await reader1.ReadAsync())
                    readings1.Add((Convert.ToDouble(reader1.GetValue(0)), reader1.GetDateTime(1)));

            using (var cmd2 = new SqlCommand("SELECT Watt, Timestamp FROM SensorData2 ORDER BY Timestamp ASC;", conn))
            using (var reader2 = await cmd2.ExecuteReaderAsync())
                while (await reader2.ReadAsync())
                    readings2.Add((Convert.ToDouble(reader2.GetValue(0)), reader2.GetDateTime(1)));

            list.AddRange(readings1);
            list.AddRange(readings2);
            list.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error reading DB: " + ex.Message);
    }
    return list;
}

(Dictionary<DateTime, double> dailyKWh, Dictionary<DateTime, double> dailyLatestWatt)
ComputeDailyKWh(List<(double Watt, DateTime Timestamp)> readings)
{
    var dailyKWh = new Dictionary<DateTime, double>();
    var dailyLatest = new Dictionary<DateTime, double>();

    if (readings == null || readings.Count == 0)
        return (dailyKWh, dailyLatest);

    // Group by day
    var grouped = readings
        .GroupBy(r => r.Timestamp.Date)
        .OrderBy(g => g.Key);

    foreach (var dayGroup in grouped)
    {
        double minWatt = dayGroup.Min(r => r.Watt);
        double maxWatt = dayGroup.Max(r => r.Watt);

        double kwh = maxWatt - minWatt;  // MERALCO-style reading

        dailyKWh[dayGroup.Key] = kwh;

        // Latest watt for the day
        var latestRecord = dayGroup.OrderByDescending(r => r.Timestamp).First();
        dailyLatest[dayGroup.Key] = latestRecord.Watt;
    }

    return (dailyKWh, dailyLatest);
}




// ---------- API ENDPOINTS ----------

// /api/latest?appliance=1|2|0
app.MapGet("/api/latest", async (HttpRequest request) =>
{
    int appliance = 0;
    if (request.Query.ContainsKey("appliance") && int.TryParse(request.Query["appliance"], out var a)) appliance = a;

    double watt = 0;
    DateTime ts = DateTime.MinValue;

    try
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        if (appliance == 1 || appliance == 2)
        {
            string q = appliance == 1
                ? "SELECT TOP 1 Watt, Timestamp FROM SensorData ORDER BY Timestamp DESC;"
                : "SELECT TOP 1 Watt, Timestamp FROM SensorData2 ORDER BY Timestamp DESC;";

            using var cmd = new SqlCommand(q, conn);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                watt = Convert.ToDouble(reader.GetValue(0));
                ts = reader.GetDateTime(1);
            }
        }
        else
        {
            string q1 = "SELECT TOP 1 Watt, Timestamp FROM SensorData ORDER BY Timestamp DESC;";
            string q2 = "SELECT TOP 1 Watt, Timestamp FROM SensorData2 ORDER BY Timestamp DESC;";

            double w1 = 0, w2 = 0;
            DateTime t1 = DateTime.MinValue, t2 = DateTime.MinValue;

            using (var cmd1 = new SqlCommand(q1, conn))
            using (var r1 = await cmd1.ExecuteReaderAsync())
                if (await r1.ReadAsync()) { w1 = Convert.ToDouble(r1.GetValue(0)); t1 = r1.GetDateTime(1); }

            using (var cmd2 = new SqlCommand(q2, conn))
            using (var r2 = await cmd2.ExecuteReaderAsync())
                if (await r2.ReadAsync()) { w2 = Convert.ToDouble(r2.GetValue(0)); t2 = r2.GetDateTime(1); }

            watt = w1 + w2;
            ts = t1 > t2 ? t1 : t2;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error /api/latest: " + ex.Message);
        return Results.Json(new { message = "No data" });
    }

    return Results.Json(new { watt, timestamp = ts });
});

// GET /api/total?appliance=1|2|0&start=yyyy-MM-dd&end=yyyy-MM-dd&rate=...
app.MapGet("/api/records", async (HttpRequest request) =>
{
    double ratePerKWh = DefaultRatePerKWh;
    if (request.Query.ContainsKey("rate") && double.TryParse(request.Query["rate"], out var rr))
        ratePerKWh = rr;

    int appliance = 0;
    if (request.Query.ContainsKey("appliance") && int.TryParse(request.Query["appliance"], out var a))
        appliance = a;

    DateTime? monthFilter = null;
    if (request.Query.ContainsKey("month") && DateTime.TryParse(request.Query["month"] + "-01", out var m))
        monthFilter = new DateTime(m.Year, m.Month, 1);

    var readings = await GetReadingsOrderedAsync(appliance);

    // --- DAILY KWH using trapezoidal integration ---
    var dailyKWhMap = new Dictionary<DateTime, double>();
    var dailyLatestMap = new Dictionary<DateTime, double>();

    (dailyKWhMap, dailyLatestMap) = ComputeDailyKWh(readings);

    // Apply month filter
    if (monthFilter.HasValue)
    {
        dailyKWhMap = dailyKWhMap
            .Where(kv => kv.Key.Year == monthFilter.Value.Year && kv.Key.Month == monthFilter.Value.Month)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    var records = new List<object>();

    foreach (var date in dailyKWhMap.Keys.OrderByDescending(d => d))
    {
        double kwh = Math.Round(dailyKWhMap[date], 6);
        if (kwh <= 0) continue;

        double latest = dailyLatestMap.ContainsKey(date) ? dailyLatestMap[date] : 0;
        double cost = Math.Round(kwh * ratePerKWh, 4);

        records.Add(new { date, latestWatt = latest, kWh = kwh, totalCost = cost });
    }

    return Results.Json(new { ratePerKWh, records });
});


app.Run();
