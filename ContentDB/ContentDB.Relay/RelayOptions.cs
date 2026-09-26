// ContentDB.Relay —— 中继配置

namespace ContentDB.Relay;

public sealed class RelayOptions
{
	public const string SectionName = "Relay";

	/// <summary>控制面 HTTP 监听地址(host:port)。</summary>
	public string Listen { get; set; } = "http://127.0.0.1:5180";

	/// <summary>内部调用密钥(主后端 /allocate 时经 X-Relay-Secret 头携带)。为空时拒绝所有分配。</summary>
	public string InternalSecret { get; set; } = "";

	/// <summary>UDP 数据面绑定地址;空 = 所有网卡。</summary>
	public string UdpBindAddress { get; set; } = "";

	/// <summary>对外公布的公网地址(launcher 连接用,如 relay.luanti.cn)。</summary>
	public string PublicAddress { get; set; } = "";

	/// <summary>UDP 端口池(每个房间占一个)。</summary>
	public int UdpPortStart { get; set; } = 20000;
	public int UdpPortEnd { get; set; } = 25000;

	/// <summary>最大房间数。</summary>
	public int MaxRooms { get; set; } = 500;

	/// <summary>分配后未注册 Host 的回收宽限(秒)。</summary>
	public int RegisterGraceSeconds { get; set; } = 30;

	/// <summary>房间空闲回收(秒,无任何 UDP 活动即回收)。</summary>
	public int RoomIdleSeconds { get; set; } = 60;

	/// <summary>Guest 端点静默剔除(秒)。</summary>
	public int GuestIdleSeconds { get; set; } = 30;

	/// <summary>每房间最大 Guest 数。</summary>
	public int MaxGuestsPerRoom { get; set; } = 8;

	/// <summary>每房间限速(字节/秒;默认 5 Mbps)。</summary>
	public int RateLimitBytesPerSecond { get; set; } = 640 * 1024;
}
