using System;

namespace GeoPositioning
{
    public static class DistanceCalculator
    {
        private const double R = 6371000.0;

        /// <param name="lat1">Origin latitude (degrees, WGS84)</param>
        /// <param name="lon1">Origin longitude (degrees, WGS84)</param>
        /// <param name="alt1">Origin altitude (m, MSL)</param>
        /// <param name="lat2">Target latitude (degrees, WGS84)</param>
        /// <param name="lon2">Target longitude (degrees, WGS84)</param>
        /// <param name="alt2">Target altitude (m, MSL)</param>
        /// <param name="distance">3D distance between the two points (m)</param>
        public static bool Calculate(
            double? lat1,
            double? lon1,
            double? alt1,
            double? lat2,
            double? lon2,
            double? alt2,
            out double distance)
        {
            distance = 0.0;

            if (!lat1.HasValue || !lon1.HasValue || !alt1.HasValue ||
                !lat2.HasValue || !lon2.HasValue || !alt2.HasValue)
                return false;

            double lat1Rad = ToRad(lat1.Value);
            double lat2Rad = ToRad(lat2.Value);
            double dLat    = ToRad(lat2.Value - lat1.Value);
            double dLon    = ToRad(lon2.Value - lon1.Value);

            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(lat1Rad) * Math.Cos(lat2Rad)
                     * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double horizontal = R * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
            double dAlt       = alt2.Value - alt1.Value;

            distance = Math.Sqrt(horizontal * horizontal + dAlt * dAlt);
            return true;
        }

        private static double ToRad(double deg) => deg * Math.PI / 180.0;
    }
}
