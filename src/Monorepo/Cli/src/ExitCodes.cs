namespace _42.Monorepo.Cli
{
    public static class ExitCodes
    {
        public const int SUCCESS = 0;

        public const int ERROR_INPUT_PARSING = -1;
        public const int ERROR_CRASH = 1;
        public const int ERROR_WRONG_INPUT = 2;
        public const int ERROR_WRONG_PLACE = 3;

        public const int WARNING_ABORTED = 4;
        public const int WARNING_NO_WORK_NEEDED = 42;
    }
}
