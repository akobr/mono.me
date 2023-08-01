using System;

namespace _42.CLI.Toolkit
{
    public static class ParsingErrorResponseMessages
    {
        private static readonly string[] Messages =
        {
            "are your fingers itchy",
            "do you need a brain surgery",
            "too many beers yesterday",
            "is the keyboard broken",
            "probably a bluetooth signal disruption",
            "should I give you a slap",
            "too many coffees",
            "should I call your mom",
            "maybe your keyboard battery is low",
            "is there a wrong guy between the chair and keyboard",
            "did you try to turn it off and on again",
        };

        public static string GetRandom()
        {
            return Messages[Random.Shared.Next(Messages.Length)];
        }
    }
}
