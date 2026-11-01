using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Ajustes de rendimiento que no se guardan en Project Settings o que son distintos en el editor y en el build.
public static class ConfiguracionRendimiento
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Aplicar()
    {
        // Mip streaming (Quality ▸ PC): con un presupuesto holgado Unity no descarta nunca los mips que ya ha subido
        // aunque no se vean; así solo quedan en memoria los que pide la vista actual (en las vistas medidas, 190-310 MB
        // en lugar de 530 MB a 1080p) sin bajar nunca la calidad, porque el presupuesto (1 GB) sigue sin agotarse
        Texture.streamingTextureDiscardUnusedMips = true;

#if !UNITY_EDITOR
        // GPU Resident Drawer: el asset de URP lo lleva puesto porque en el editor hace la vista de escena ~3 veces más
        // ligera (la CPU deja de preparar 32.000 renderers uno a uno). En el build, con una GPU integrada (Radeon 780M)
        // que ya va al límite, cuesta ~1 ms de GPU por frame y se quita; con una gráfica dedicada, donde manda la CPU,
        // se puede activar arrancando con «-grd 1»
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-grd");
        bool grd = i >= 0 && i + 1 < args.Length && args[i + 1] == "1";
        if (!grd && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            urp.gpuResidentDrawerMode = GPUResidentDrawerMode.Disabled;
#endif
    }
}
