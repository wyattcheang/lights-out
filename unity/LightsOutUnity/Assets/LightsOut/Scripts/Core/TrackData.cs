// Circuit outlines (bacinger/f1-circuits, MIT) in local metres, with elevation (metres) sampled
// from public F1 timing telemetry where available. Coordinates: x east, y south (screen-style).
using System;

namespace LightsOut
{
    [Serializable]
    public class TrackJson
    {
        public string id;
        public string city;
        public string country;
        public int len;
        public int opened;
        public bool cur;
        public float[] p;   // x0,y0,x1,y1,...
        public float[] e;   // elevation per point (may be empty)
    }

    [Serializable]
    public class TrackList
    {
        public TrackJson[] tracks;
    }
}
