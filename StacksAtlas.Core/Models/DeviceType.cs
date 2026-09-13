namespace StacksAtlas.Core.Models;

/// <summary>
/// Defines granular device categories for professional Pro-AV and IT environments.
/// </summary>
public enum DeviceType
{
    // General
    Unknown,
    Computer,
    Laptop,
    Workstation,
    Server,
    VirtualMachine,
    Mobile,
    Tablet,
    Phone_VoIP,
    IoT,

    // Networking
    Network_Infrastructure,
    Network_Router,
    Network_Switch,
    Network_AP,
    Network_Gateway,
    Network_Controller,
    Network_Firewall,

    // Audio
    Audio_Endpoint,
    Audio_DSP,
    Audio_Microphone,
    Audio_Speaker,
    Audio_Soundbar,
    Audio_Amplifier,
    Audio_AVReceiver,
    Audio_Console,
    Audio_Interface,
    Audio_DanteDevice,

    // Video & Display
    Video_Display,
    Video_Projector,
    Video_Camera,
    Video_PTZ,
    Video_Switcher,
    Video_Matrix,
    Video_Encoder,
    Video_Decoder,
    Video_NVR,
    Video_WallController,
    Digital_Signage,
    Media_Player,
    Streaming_Device,

    // Control & Collaboration
    Control_Processor,
    Control_TouchPanel,
    Collaboration_RoomSystem,
    Collaboration_Codec,
    Lighting_Controller,

    // Power
    Power_UPS,
    Power_PDU,

    // Peripherals
    Printer,
    Storage_NAS,
    Gaming_Console,
    Custom
}
