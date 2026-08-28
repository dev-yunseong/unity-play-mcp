using System;
using System.Collections.Generic;
using System.Globalization;
using Artel.Protocol;

namespace Artel.Capture
{
    /// <summary>
    /// What one `capture_screen` call asks for, after the JSON-RPC params have been read.
    /// </summary>
    internal struct CaptureRequest
    {
        /// <summary>Null captures the whole screen.</summary>
        public int? TargetId;

        public int MaxEdge;

        /// <summary>Extra pixels around a cropped element. Ignored for a full screen.</summary>
        public float Padding;

        public bool IsFullScreen { get { return !TargetId.HasValue; } }

        /// <summary>
        /// A full screen goes out as JPEG; a crop as PNG.
        /// </summary>
        /// <remarks>
        /// A crop is usually UI, where JPEG's ringing lands on exactly what is being judged —
        /// glyph edges and element borders. A full screen is mostly rendered scene and large
        /// enough that lossless would cost far more than the detail is worth.
        /// </remarks>
        public bool UsePng { get { return !IsFullScreen; } }

        public string ContentType { get { return UsePng ? "image/png" : "image/jpeg"; } }
    }

    /// <summary>
    /// Reads `capture_screen` params. Shape: [], [targetId], or [targetId, options].
    /// </summary>
    internal static class CaptureRequestReader
    {
        /// <summary>The longest edge a whole-screen capture is stored at.</summary>
        public const int FullScreenMaxEdge = 1024;

        /// <summary>The longest edge a cropped element is stored at.</summary>
        public const int CropMaxEdge = 512;

        public const float DefaultPadding = 8f;

        /// <summary>JPEG quality for whole-screen captures.</summary>
        public const int JpegQuality = 70;

        public static bool TryRead(List<object> parameters, out CaptureRequest request, out string error)
        {
            request = new CaptureRequest
            {
                TargetId = null,
                MaxEdge = FullScreenMaxEdge,
                Padding = 0f
            };
            error = null;

            if (parameters == null || parameters.Count == 0)
            {
                return true;
            }

            if (parameters[0] != null)
            {
                if (!TryReadInt(parameters[0], out var targetId))
                {
                    error = "capture_screen params are [] or [targetId] or [targetId, options].";
                    return false;
                }

                request.TargetId = targetId;
                request.MaxEdge = CropMaxEdge;
                request.Padding = DefaultPadding;
            }

            if (parameters.Count < 2 || parameters[1] == null)
            {
                return true;
            }

            if (!ActionParamsObject.TryRead(parameters[1], out var options))
            {
                error = "capture_screen options must be an object.";
                return false;
            }

            object value;
            if (options.TryGetValue("maxEdge", out value) && value != null)
            {
                if (!TryReadInt(value, out var maxEdge) || maxEdge <= 0)
                {
                    error = "capture_screen maxEdge must be a positive integer.";
                    return false;
                }

                request.MaxEdge = maxEdge;
            }

            if (options.TryGetValue("padding", out value) && value != null)
            {
                if (!TryReadFloat(value, out var padding) || padding < 0f)
                {
                    error = "capture_screen padding must be zero or greater.";
                    return false;
                }

                request.Padding = padding;
            }

            return true;
        }

        private static bool TryReadInt(object value, out int result)
        {
            result = 0;
            if (value == null)
            {
                return false;
            }

            if (value is long longValue)
            {
                if (longValue < int.MinValue || longValue > int.MaxValue)
                {
                    return false;
                }

                result = (int)longValue;
                return true;
            }

            if (value is int intValue)
            {
                result = intValue;
                return true;
            }

            return int.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out result);
        }

        private static bool TryReadFloat(object value, out float result)
        {
            result = 0f;
            return value != null &&
                   float.TryParse(
                       Convert.ToString(value, CultureInfo.InvariantCulture),
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out result) &&
                   !float.IsNaN(result) &&
                   !float.IsInfinity(result);
        }
    }
}
