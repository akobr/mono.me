namespace _42.Platform.Storyteller.DbCreator;

public static class Constants
{
    public const string APPLICATION_CONFIG_JSON = "app.config.json";
    public const string DIRECTORY_SEPARATOR = "/";
    public const string ORGANIZATION = "42dotnet";

    // The owner's pairwise Entra ID sub from before 424-106; under Entra ID it no longer resolves. Set baseAccount:id to the
    // owner's lower-case {tid}:{oid} (Entra ID), or to the sub (AuthKit or Keycloak).
    public const string LEGACY_BASE_ACCOUNT_ID = "AAAAAAAAAAAAAAAAAAAAAB_bcwm5bTkO0iWu1X6dccA";
    public const string BASE_ACCOUNT_USER_NAME = "kobr.ales@outlook.com";
    public const string BASE_ACCOUNT_NAME = "Aleš Kobr";
}
