using GMap.NET;

namespace MissionPlanner.Utilities
{
    public class NotamBriefingItem
    {
        public string NotamKey { get; set; }
        public string Type { get; set; }
        public string Title { get; set; }
        public string State { get; set; }
        public string LastModified { get; set; }
        public PointLatLng Center { get; set; }
        public string DetailUrl { get; set; }
        public string DetailText { get; set; }

        public string DisplayId =>
            string.IsNullOrEmpty(NotamKey) ? "—" : NotamKey.Split('-')[0];

        public override string ToString() => DisplayId + " — " + Title;
    }
}
