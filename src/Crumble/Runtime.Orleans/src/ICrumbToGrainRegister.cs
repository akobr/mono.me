namespace _42.Crumble;

public interface ICrumbToGrainRegister
{
    ICrumbToGrainRegister RegisterCrumb(string crumbKey, Type grainType);
}
