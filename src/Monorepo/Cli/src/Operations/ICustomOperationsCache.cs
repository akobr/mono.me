namespace _42.Monorepo.Cli.Operations
{
    public interface ICustomOperationsCache
    {
        public bool TryGetCustomValue(string itemKey, string valueKey, out object value);

        public void StoreValue(string itemKey, string valueKey, object value);
    }
}
