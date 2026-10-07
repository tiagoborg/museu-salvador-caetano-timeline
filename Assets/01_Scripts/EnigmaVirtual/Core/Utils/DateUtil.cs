using System;
using System.Globalization;

namespace EnigmaVirtual
{
    public static class DateUtil
    {
        static CultureInfo culture = new CultureInfo("pt-PT");

        /// <summary>
        /// Date should be of format dd-mm-yyyy hh:mm:ss
        /// </summary>
        /// <param name="date"></param>
        /// <param name="doubleLine"></param>
        /// <returns></returns>
        public static string GetMultilineDate(string date, bool doubleLine)
        {
            string[] parts = date.Split(' ');
            string[] time = parts[1].Split(':');
            string shortDate = parts[0] + "  " + (doubleLine ? "\n<size=30>" : "") + time[0] + ":" + time[1] + (doubleLine ? "</size>" : "");
            return shortDate;
        }

        /// <summary>
        /// Date should be of format dd-mm-yyyy hh:mm:ss
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static string GetNewsDate(string date)
        {
            DateTime dt = DateTime.Now;
            string month = "Janeiro 00";
            if (DateTime.TryParse(date, out dt))
                month = dt.ToString("MMM dd hh:mm", culture);
            return month;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static string GetDate(string date)
        {
            string[] parts = date.Split(' ');
            return parts[0];
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static string ReverseDate(string date)
        {
            string[] parts = date.Split('-');
            if (parts.Length < 3) return "";
            string newDate = parts[2] + "-" + parts[1] + "-" + parts[0];
            return newDate;
        }


        /// <summary>
        /// Date should be of format dd-mm-yyyy
        /// </summary>
        /// <param name="date"></param>
        /// <param name="yearIndex"></param>
        /// <param name="divider"></param>
        /// <returns></returns>
        public static string GetYear(string date, int yearIndex = 2, string divider = "-")
        {
            string[] parts = date.Split(divider[0]);
            return parts.Length == 0 ? "0" : parts[yearIndex];
        }

        /// <summary>
        /// Date should be of format dd-mm-yyyy
        /// </summary>
        /// <param name="date"></param>
        /// <param name="monthIndex"></param>
        /// <param name="divider"></param>
        /// <returns></returns>
        public static string GetMonth(string date, int monthIndex = 1, string divider = "-")
        {
            string[] parts = date.Split(divider[0]);
            return parts.Length == 0 ? "0" : parts[monthIndex];
        }

        /// <summary>
        /// Date should be of format dd-mm-yyyy
        /// </summary>
        /// <param name="date"></param>
        /// <param name="monthIndex"></param>
        /// <param name="divider"></param>
        /// <returns></returns>
        public static string GetDay(string date, int dayIndex = 0, string divider = "-")
        {
            string[] parts = date.Split(divider[0]);
            return parts.Length == 0 ? "0" : parts[dayIndex];
        }


        /// <summary>
        /// Date should be of format HH:MM:SS or HH:MM
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static string GetHours(string date)
        {
            string[] parts = date.Split(':');
            return parts.Length == 0 ? "0" : parts[0];
        }

        /// <summary>
        /// Date should be of format HH:MM
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static string GetMinutes(string date)
        {
            string[] parts = date.Split(':');
            return parts.Length < 1 ? "0" : parts[1];
        }

        /// <summary>
        /// Date should be of format HH:MM
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static string GetSeconds(string date)
        {
            string[] parts = date.Split(':');
            return parts.Length < 2 ? "0" : parts[2];
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        public static DateTime TimestampToDate(double timestamp)
        {
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            return origin.AddSeconds(timestamp);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static double Timestamp(DateTime date)
        {
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            TimeSpan diff = date - origin;
            return Math.Floor(diff.TotalSeconds);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static string GetTime(string date)
        {
            string[] parts = date.Split(' ');
            return parts.Length < 1 ? "00:00:00" : parts[1];
        }
    }
}