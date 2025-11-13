using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text.Json;

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
        public string? RawStravaDataJson { get; set; }  // JSON string for RawStravaData field
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
            // Read header to get all column names
            csv.Read();
            csv.ReadHeader();
            var headers = csv.HeaderRecord ?? Array.Empty<string>();

            // Read all rows
            while (csv.Read())
            {
                var record = new StravaActivityRecord();
                var rawData = new Dictionary<string, object?>();

                // Parse known fields
                record.ActivityId = csv.GetField("Activity ID") ?? string.Empty;
                record.ActivityDate = csv.GetField("Activity Date") ?? string.Empty;
                record.ActivityName = csv.GetField("Activity Name") ?? string.Empty;
                record.ActivityType = csv.GetField("Activity Type") ?? string.Empty;
                record.ActivityDescription = csv.GetField("Activity Description") ?? string.Empty;
                record.Filename = csv.GetField("Filename") ?? string.Empty;
                record.ActivityPrivateNote = csv.GetField("Activity Private Note");
                record.Media = csv.GetField("Media");

                // Parse all other columns into raw data
                foreach (var header in headers)
                {
                    if (string.IsNullOrWhiteSpace(header)) continue;

                    // Skip columns we've already mapped
                    if (header == "Activity ID" || header == "Activity Date" || header == "Activity Name" ||
                        header == "Activity Type" || header == "Activity Description" || header == "Filename" ||
                        header == "Activity Private Note" || header == "Media")
                        continue;

                    var value = csv.GetField(header);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        // Try to parse as number if possible
                        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleVal))
                        {
                            rawData[header] = doubleVal;
                        }
                        else if (bool.TryParse(value, out var boolVal))
                        {
                            rawData[header] = boolVal;
                        }
                        else
                        {
                            rawData[header] = value;
                        }
                    }
                }

                // Build RawStravaData JSON
                var rawStravaData = new
                {
                    activityId = record.ActivityId,
                    elapsedTime = rawData.ContainsKey("Elapsed Time") ? rawData["Elapsed Time"] : null,
                    movingTime = rawData.ContainsKey("Moving Time") ? rawData["Moving Time"] : null,
                    distance = rawData.ContainsKey("Distance") ? rawData["Distance"] : null,
                    maxHeartRate = rawData.ContainsKey("Max Heart Rate") ? rawData["Max Heart Rate"] : null,
                    avgHeartRate = rawData.ContainsKey("Average Heart Rate") ? rawData["Average Heart Rate"] : null,
                    relativeEffort = rawData.ContainsKey("Relative Effort") ? rawData["Relative Effort"] : null,
                    maxSpeed = rawData.ContainsKey("Max Speed") ? rawData["Max Speed"] : null,
                    avgSpeed = rawData.ContainsKey("Average Speed") ? rawData["Average Speed"] : null,
                    elevationLoss = rawData.ContainsKey("Elevation Loss") ? rawData["Elevation Loss"] : null,
                    elevationLow = rawData.ContainsKey("Elevation Low") ? rawData["Elevation Low"] : null,
                    elevationHigh = rawData.ContainsKey("Elevation High") ? rawData["Elevation High"] : null,
                    maxGrade = rawData.ContainsKey("Max Grade") ? rawData["Max Grade"] : null,
                    avgGrade = rawData.ContainsKey("Average Grade") ? rawData["Average Grade"] : null,
                    avgPosGrade = rawData.ContainsKey("Average Positive Grade") ? rawData["Average Positive Grade"] : null,
                    avgNegGrade = rawData.ContainsKey("Average Negative Grade") ? rawData["Average Negative Grade"] : null,
                    maxCadence = rawData.ContainsKey("Max Cadence") ? rawData["Max Cadence"] : null,
                    avgCadence = rawData.ContainsKey("Average Cadence") ? rawData["Average Cadence"] : null,
                    maxWatts = rawData.ContainsKey("Max Watts") ? rawData["Max Watts"] : null,
                    avgWatts = rawData.ContainsKey("Average Watts") ? rawData["Average Watts"] : null,
                    calories = rawData.ContainsKey("Calories") ? rawData["Calories"] : null,
                    maxTemperature = rawData.ContainsKey("Max Temperature") ? rawData["Max Temperature"] : null,
                    avgTemperature = rawData.ContainsKey("Average Temperature") ? rawData["Average Temperature"] : null,
                    totalSteps = rawData.ContainsKey("Total Steps") ? rawData["Total Steps"] : null,
                    totalGrit = rawData.ContainsKey("Total Grit") ? rawData["Total Grit"] : null,
                    avgFlow = rawData.ContainsKey("Average Flow") ? rawData["Average Flow"] : null,
                    gradeAdjustedDistance = rawData.ContainsKey("Grade Adjusted Distance") ? rawData["Grade Adjusted Distance"] : null,
                    gradeAdjustedPace = rawData.ContainsKey("Average Grade Adjusted Pace") ? rawData["Average Grade Adjusted Pace"] : null,
                    gear = rawData.ContainsKey("Gear") ? rawData["Gear"] : null,
                    commute = rawData.ContainsKey("Commute") ? rawData["Commute"] : null,
                    perceivedExertion = rawData.ContainsKey("Perceived Exertion") ? rawData["Perceived Exertion"] : null,
                    activityDescription = record.ActivityDescription,
                    activityPrivateNote = record.ActivityPrivateNote,
                    // Weather data
                    weather = BuildWeatherData(rawData),
                    // Store all other fields
                    otherFields = rawData.Where(kvp => !IsKnownField(kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    source = "strava_import",
                    importedAt = DateTime.UtcNow.ToString("O")
                };

                record.RawStravaDataJson = JsonSerializer.Serialize(rawStravaData, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });

                records.Add(record);
            }
        }

        return records;
    }

    private bool IsKnownField(string fieldName)
    {
        var knownFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Activity ID", "Activity Date", "Activity Name", "Activity Type", "Activity Description",
            "Filename", "Activity Private Note", "Media", "Elapsed Time", "Moving Time", "Distance",
            "Max Heart Rate", "Average Heart Rate", "Relative Effort", "Max Speed", "Average Speed",
            "Elevation Loss", "Elevation Low", "Elevation High", "Max Grade", "Average Grade",
            "Average Positive Grade", "Average Negative Grade", "Max Cadence", "Average Cadence",
            "Max Watts", "Average Watts", "Calories", "Max Temperature", "Average Temperature",
            "Total Steps", "Total Grit", "Average Flow", "Grade Adjusted Distance",
            "Average Grade Adjusted Pace", "Gear", "Commute", "Perceived Exertion"
        };
        return knownFields.Contains(fieldName) || fieldName.StartsWith("Weather ", StringComparison.OrdinalIgnoreCase);
    }

    private Dictionary<string, object?>? BuildWeatherData(Dictionary<string, object?> rawData)
    {
        var weather = new Dictionary<string, object?>();
        bool hasWeather = false;

        if (rawData.ContainsKey("Weather Observation Time"))
        {
            weather["observationTime"] = rawData["Weather Observation Time"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Weather Condition"))
        {
            weather["condition"] = rawData["Weather Condition"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Weather Temperature"))
        {
            weather["temperature"] = rawData["Weather Temperature"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Apparent Temperature"))
        {
            weather["apparentTemperature"] = rawData["Apparent Temperature"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Dewpoint"))
        {
            weather["dewpoint"] = rawData["Dewpoint"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Humidity"))
        {
            weather["humidity"] = rawData["Humidity"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Weather Pressure"))
        {
            weather["pressure"] = rawData["Weather Pressure"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Wind Speed"))
        {
            weather["windSpeed"] = rawData["Wind Speed"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Wind Gust"))
        {
            weather["windGust"] = rawData["Wind Gust"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Wind Bearing"))
        {
            weather["windDirection"] = rawData["Wind Bearing"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Precipitation Intensity"))
        {
            weather["precipitationIntensity"] = rawData["Precipitation Intensity"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Precipitation Probability"))
        {
            weather["precipitationProbability"] = rawData["Precipitation Probability"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Precipitation Type"))
        {
            weather["precipitationType"] = rawData["Precipitation Type"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Cloud Cover"))
        {
            weather["cloudCover"] = rawData["Cloud Cover"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Weather Visibility"))
        {
            weather["visibility"] = rawData["Weather Visibility"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("UV Index"))
        {
            weather["uvIndex"] = rawData["UV Index"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Weather Ozone"))
        {
            weather["ozone"] = rawData["Weather Ozone"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Sunrise Time"))
        {
            weather["sunriseTime"] = rawData["Sunrise Time"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Sunset Time"))
        {
            weather["sunsetTime"] = rawData["Sunset Time"];
            hasWeather = true;
        }
        if (rawData.ContainsKey("Moon Phase"))
        {
            weather["moonPhase"] = rawData["Moon Phase"];
            hasWeather = true;
        }

        return hasWeather ? weather : null;
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

