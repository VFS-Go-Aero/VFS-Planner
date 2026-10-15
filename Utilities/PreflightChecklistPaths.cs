using System;
using System.IO;

namespace MissionPlanner.Utilities
{
    /// <summary>
    /// Shipped checklist XML next to the executable (see MissionPlanner.csproj CopyToOutputDirectory).
    /// Stage 2: Stage2Checklist.xml · Stage 3: Stage3Checklist.xml
    /// </summary>
    public static class PreflightChecklistPaths
    {
        public const string Stage2TemplateFileName = "Stage2Checklist.xml";
        public const string Stage3TemplateFileName = "Stage3Checklist.xml";

        public static string ResolveDefaultForStage(FlightOperationStage stage)
        {
            var running = Settings.GetRunningDirectory();
            switch (stage)
            {
                case FlightOperationStage.Stage2:
                    return Path.Combine(running, Stage2TemplateFileName);
                case FlightOperationStage.Stage3:
                    return Path.Combine(running, Stage3TemplateFileName);
                default:
                    return Path.Combine(running, Stage2TemplateFileName);
            }
        }

        public static string ResolveGeneralDefault()
        {
            return ResolveDefaultForStage(FlightOperationStage.Stage2);
        }

        public static string UserConfigPathForStage(FlightOperationStage stage)
        {
            var dir = Settings.GetUserDataDirectory();
            switch (stage)
            {
                case FlightOperationStage.Stage2:
                    return Path.Combine(dir, "missionChecklist_stage2.xml");
                case FlightOperationStage.Stage3:
                    return Path.Combine(dir, "missionChecklist_stage3.xml");
                default:
                    return Path.Combine(dir, "missionChecklist.xml");
            }
        }

        /// <summary>
        /// Prefer a newer shipped template over a saved user checklist so rebuilds pick up XML edits.
        /// </summary>
        public static string ResolveLoadPath(string userConfigPath, string templatePath)
        {
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
                return userConfigPath;

            if (string.IsNullOrEmpty(userConfigPath) || !File.Exists(userConfigPath))
                return templatePath;

            var userName = Path.GetFileName(userConfigPath);
            if (userName.Equals("checklist.xml", StringComparison.OrdinalIgnoreCase) ||
                userName.Equals("checklistDefault.xml", StringComparison.OrdinalIgnoreCase) ||
                userName.Equals(Stage2TemplateFileName, StringComparison.OrdinalIgnoreCase) ||
                userName.Equals(Stage3TemplateFileName, StringComparison.OrdinalIgnoreCase) ||
                userName.Equals("checklistStage3Default.xml", StringComparison.OrdinalIgnoreCase) ||
                userName.Equals("missionChecklist.xml", StringComparison.OrdinalIgnoreCase))
                return templatePath;

            try
            {
                if (File.GetLastWriteTimeUtc(templatePath) > File.GetLastWriteTimeUtc(userConfigPath))
                    return templatePath;
            }
            catch
            {
            }

            return userConfigPath;
        }
    }
}
