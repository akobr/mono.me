namespace _42.Crumble;

public interface ICrumbToGrainRegister
{
    void RegisterCrumb(string crumbKey, Type grainType);
}
