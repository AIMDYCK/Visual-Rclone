namespace RcloneCommanderAdvanced.Models;

/// <summary>
/// Modo en el que se expone una unidad montada por rclone en Windows.
/// El usuario elige uno por cada disco desde la tarjeta del dashboard.
/// </summary>
public enum MountMode
{
    /// <summary>
    /// Unidad de red (comportamiento clasico de rclone en Windows).
    /// Se anade el flag <c>--network-mode</c> a la linea de comandos.
    ///
    /// <para><b>Pros:</b> no requiere WinFsp en modo disco local, arranque mas
    /// rapido, tolerante a desconexiones y no bloquea el arranque del sistema.
    /// Es el modo recomendado para uso general y acceso remoto.</para>
    ///
    /// <para><b>Contras:</b> algunas aplicaciones antiguas no la tratan como
    /// disco real, no admite ciertas operaciones de bajo nivel (formateo,
    /// indexado de Windows Search) y el rendimiento en copias masivas es algo
    /// menor.</para>
    /// </summary>
    NetworkDrive = 0,

    /// <summary>
    /// Disco fisico / local (se omite <c>--network-mode</c>).
    /// Windows la presenta como una unidad local mas.
    ///
    /// <para><b>Pros:</b> maxima compatibilidad con cualquier aplicacion,
    /// permite indexado de Windows Search, papelera de reciclaje y operaciones
    /// de archivo nativas; mejor rendimiento en transferencias grandes.</para>
    ///
    /// <para><b>Contras:</b> requiere WinFsp correctamente instalado, el
    /// arranque puede ser mas lento, es mas sensible a cortes de red (puede
    /// congelar el Explorador) y algunas apps pueden intentar escribir
    /// metadatos NTFS que el remoto no soporta.</para>
    /// </summary>
    PhysicalDisk = 1
}
