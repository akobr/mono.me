namespace _42.Monorepo.Cli.Operations
{
    public interface IOpStrategiesRegister
    {
        void RegisterActionStrategy<T>(string operationKey)
            where T : class, IOpStrategy;

        void RegisterFuncStrategy<T>(string operationKey)
            where T : class, IOpStrategyGeneric;
    }
}
