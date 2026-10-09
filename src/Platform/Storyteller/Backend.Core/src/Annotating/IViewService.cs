namespace _42.Platform.Storyteller.Annotating;

// Views stay implicit for writes: any view name can hold data. The registry adds descriptions and lets
// clients list the views of a project without scanning it.
public interface IViewService
{
    // Registered views and "default". With discover, also views found in the project's data.
    Task<IReadOnlyList<View>> GetViewsAsync(string organization, string project, bool discover);

    Task<View> CreateViewAsync(string organization, string project, ViewCreate model, string author);

    // Updates the description, and registers the view when it is not registered yet.
    Task<View> UpdateViewAsync(string organization, string project, string view, ViewUpdate model, string author);
}
