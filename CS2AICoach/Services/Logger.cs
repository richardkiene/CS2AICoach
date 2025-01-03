namespace CS2AICoach.Services
{
    public static class Logger
    {
        private static bool _verbose = false;

        public static void SetVerboseMode(bool verbose)
        {
            _verbose = verbose;
        }

        public static void Log(string message, bool alwaysShow = false)
        {
            if (_verbose || alwaysShow)
            {
                Console.WriteLine(message);
            }
        }
    }
}