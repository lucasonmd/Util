using System;

namespace GeoPositioning
{
    public class TargetLocation
    {
        public double? Latitude  { get; set; }
        public double? Longitude { get; set; }
        public double? Altitude  { get; set; }
    }

    public static class PositionEstimator
    {
        private const double A  = 6378137.0;
        private const double F  = 1.0 / 298.257223563;
        private const double E2 = 2 * F - F * F;

        /// <param name="vehicleLat">Vehicle latitude (degrees, WGS84)</param>
        /// <param name="vehicleLon">Vehicle longitude (degrees, WGS84)</param>
        /// <param name="vehicleAlt">Vehicle altitude (m, MSL)</param>
        /// <param name="vehicleRoll">Vehicle roll (degrees)</param>
        /// <param name="vehiclePitch">Vehicle pitch (degrees)</param>
        /// <param name="vehicleYaw">Vehicle yaw (degrees, clockwise from North)</param>
        /// <param name="mountYaw">Platform mount yaw (degrees, relative to vehicle body)</param>
        /// <param name="sensorYaw">Sensor azimuth (degrees, relative to mount)</param>
        /// <param name="sensorPitch">Sensor elevation (degrees, positive up)</param>
        /// <param name="distance">Measured range to target (m)</param>
        public static TargetLocation Calculate(
            double? vehicleLat,
            double? vehicleLon,
            double? vehicleAlt,
            double? vehicleRoll,
            double? vehiclePitch,
            double? vehicleYaw,
            double? mountYaw,
            double? sensorYaw,
            double? sensorPitch,
            double? distance)
        {
            if (!vehicleLat.HasValue  || !vehicleLon.HasValue  || !vehicleAlt.HasValue  ||
                !vehicleRoll.HasValue || !vehiclePitch.HasValue || !vehicleYaw.HasValue  ||
                !mountYaw.HasValue    || !sensorYaw.HasValue    || !sensorPitch.HasValue ||
                !distance.HasValue)
                return new TargetLocation();

            double latRad       = ToRad(vehicleLat.Value);
            double rollRad      = ToRad(vehicleRoll.Value);
            double pitchRad     = ToRad(vehiclePitch.Value);
            double yawRad       = ToRad(vehicleYaw.Value);
            double totalAzimRad = ToRad(mountYaw.Value + sensorYaw.Value);
            double elevRad      = ToRad(sensorPitch.Value);
            double d            = distance.Value;

            double cosE = Math.Cos(elevRad);
            double sinE = Math.Sin(elevRad);
            double cosA = Math.Cos(totalAzimRad);
            double sinA = Math.Sin(totalAzimRad);

            double[] vBody = { cosE * cosA, cosE * sinA, -sinE };

            double[] vNed = BodyToNed(vBody, rollRad, pitchRad, yawRad);

            double dNorth = vNed[0] * d;
            double dEast  = vNed[1] * d;
            double dDown  = vNed[2] * d;

            double sinLat = Math.Sin(latRad);
            double denom  = 1.0 - E2 * sinLat * sinLat;
            double N      = A / Math.Sqrt(denom);
            double M      = A * (1.0 - E2) / Math.Pow(denom, 1.5);

            double cosLat = Math.Cos(latRad);
            if (Math.Abs(cosLat) < 1e-10)
                return new TargetLocation();

            return new TargetLocation
            {
                Latitude  = vehicleLat.Value + ToDeg(dNorth / M),
                Longitude = vehicleLon.Value + ToDeg(dEast  / (N * cosLat)),
                Altitude  = vehicleAlt.Value - dDown
            };
        }

        private static double[] BodyToNed(double[] v, double roll, double pitch, double yaw)
        {
            double cr = Math.Cos(roll),  sr = Math.Sin(roll);
            double cp = Math.Cos(pitch), sp = Math.Sin(pitch);
            double cy = Math.Cos(yaw),   sy = Math.Sin(yaw);

            double r00 = cy * cp;
            double r01 = cy * sp * sr - sy * cr;
            double r02 = cy * sp * cr + sy * sr;
            double r10 = sy * cp;
            double r11 = sy * sp * sr + cy * cr;
            double r12 = sy * sp * cr - cy * sr;
            double r20 = -sp;
            double r21 = cp * sr;
            double r22 = cp * cr;

            return new double[]
            {
                r00 * v[0] + r01 * v[1] + r02 * v[2],
                r10 * v[0] + r11 * v[1] + r12 * v[2],
                r20 * v[0] + r21 * v[1] + r22 * v[2]
            };
        }

        private static double ToRad(double deg) => deg * Math.PI / 180.0;
        private static double ToDeg(double rad) => rad * 180.0 / Math.PI;
    }
}
