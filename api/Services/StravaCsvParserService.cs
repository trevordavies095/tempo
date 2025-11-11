using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace Tempo.Api.Services;

public class StravaCsvParserService
{
    public class StravaActivityRecord
    {
        public string ActivityId { get; set; } = string.Empty;
        public string ActivityDate { get; set; } = string.Empty;
        public string ActivityName { get; set; } = string.Empty;
        public string ActivityType { get; set; } = string.Empty;
        public string ActivityDescription { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public string? ActivityPrivateNote { get; set; }
        public string? Media { get; set; }
    }

    public List<StravaActivityRecord> ParseActivitiesCsv(Stream csvStream)
    {
        var records = new List<StravaActivityRecord>();

        using (var reader = new StreamReader(csvStream))
        using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null // Ignore bad data rows
        }))
        {
            // Map CSV columns to our record class
            csv.Context.RegisterClassMap<StravaActivityMap>();

            try
            {
                records = csv.GetRecords<StravaActivityRecord>().ToList();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse activities.csv: {ex.Message}", ex);
            }
        }

        return records;
    }

    public List<StravaActivityRecord> GetRunActivities(List<StravaActivityRecord> allActivities)
    {
        return allActivities
            .Where(a => a.ActivityType.Equals("Run", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private sealed class StravaActivityMap : ClassMap<StravaActivityRecord>
    {
        public StravaActivityMap()
        {
            Map(m => m.ActivityId).Name("Activity ID");
            Map(m => m.ActivityDate).Name("Activity Date");
            Map(m => m.ActivityName).Name("Activity Name");
            Map(m => m.ActivityType).Name("Activity Type");
            Map(m => m.ActivityDescription).Name("Activity Description");
            Map(m => m.Filename).Name("Filename");
            Map(m => m.ActivityPrivateNote).Name("Activity Private Note");
            Map(m => m.Media).Name("Media").Optional();
        }
    }
}

