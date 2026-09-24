namespace LoupixDeck.Plugin.HwInfo;

/// <summary>
/// A single HWiNFO reading, flattened together with its parent sensor group.
/// <para>
/// HWiNFO identifies a reading stably across runs by the triple
/// (<see cref="SensorId"/>, <see cref="SensorInstance"/>, <see cref="ReadingId"/>) —
/// the section indices themselves are not stable. The HWiNFO.Sensor command
/// references a reading by exactly that triple.
/// </para>
/// <para>
/// <see cref="SensorName"/> and <see cref="Label"/> are what HWiNFO shows: translated to its UI
/// language and renamable by the user. <see cref="SensorNameOrig"/> and <see cref="LabelOrig"/> are
/// HWiNFO's own English names, the same on every system, so the plugin picks readings by those.
/// </para>
/// </summary>
public sealed record HwInfoSensor(
    HwInfoReadingType Type,
    string SensorName,
    string Label,
    string Unit,
    double Value,
    double ValueMin,
    double ValueMax,
    double ValueAvg,
    uint SensorId,
    uint SensorInstance,
    uint ReadingId,
    string SensorNameOrig = "",
    string LabelOrig = "");
