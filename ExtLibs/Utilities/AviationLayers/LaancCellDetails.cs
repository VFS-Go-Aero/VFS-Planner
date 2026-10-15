using System.Text;
using Newtonsoft.Json.Linq;

namespace MissionPlanner.Utilities.AviationLayers
{
    public static class LaancCellDetails
    {
        public static void ApplyFromProperties(JObject props, MapPolygonFeature feature)
        {
            if (feature == null || props == null)
                return;

            feature.DetailText = Format(props, feature.Id);
        }

        public static string Format(JObject props, string featureId)
        {
            var sb = new StringBuilder();
            sb.AppendLine("UAS Facility Map (LAANC reference grid)");
            sb.AppendLine("This shows FAA grid altitudes — not an authorization.");
            sb.AppendLine();

            AppendLine(sb, "Grid cell ID", props["GRID_ID"]?.ToString() ?? featureId);
            AppendLine(sb, "Object ID", props["OBJECTID"]?.ToString());
            AppendLine(sb, "Max altitude (AGL)", Alt(props["CEILING"], props["UNIT"]));
            AppendLine(sb, "Floor", Alt(props["FLOOR"], props["UNIT"]));
            AppendLine(sb, "LAANC available", props["LAANC"]?.ToString());
            AppendLine(sb, "Airport", props["APT1_NAME"]?.ToString());
            AppendLine(sb, "ICAO", props["APT1_ICAO"]?.ToString());
            AppendLine(sb, "Class", props["CLASS"]?.ToString());
            AppendLine(sb, "Timezone", props["TIMEZONE"]?.ToString());

            return sb.ToString().TrimEnd();
        }

        static void AppendLine(StringBuilder sb, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            sb.AppendLine(label + ": " + value.Trim());
        }

        static string Alt(JToken value, JToken unitToken)
        {
            if (value == null)
                return null;
            var unit = unitToken?.ToString();
            if (string.IsNullOrWhiteSpace(unit))
                unit = "ft";
            return value + " " + unit;
        }
    }
}
