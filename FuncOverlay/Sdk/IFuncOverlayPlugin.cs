namespace FuncOverlay.Sdk
{
    public interface IFuncOverlayPlugin
    {
        void OnLoad(IOverlayContext context);
        void OnRender();
        void OnUnload();
    }

    public interface IOverlayContext
    {
        string PluginDirectory { get; }
        void Log(string message);
    }
}
