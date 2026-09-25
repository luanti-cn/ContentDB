# 客户端实现指南(LuantiCN)

面向 LuantiCN 客户端 / 启动器开发者。API 细节见 [client-api.md](client-api.md),本文讲**怎么实现**。

## 0. 总体架构

```
┌─────────────────────────────────────────────────┐
│ LuantiCN Launcher(桌面程序,C# 建议)            │
│  · 设备配对 / token 安全存储                      │
│  · 进服凭证(取/生成/改名回写)                    │
│  · 心跳线程、好友面板、组队面板                    │
│  · 拉起 Luanti 客户端进程                         │
├─────────────────────────────────────────────────┤
│ Luanti 客户端(引擎)                             │
│  方案 A:不改 —— 用命令行参数自动进服(推荐起步) │
│  方案 B:小改 —— 改密回调、进服失败原因上抛        │
├─────────────────────────────────────────────────┤
│ 服务器端上报 mod(给服务器主装,Lua)              │
│  · 每 60s 向 luanti.cn 上报在线人数               │
└─────────────────────────────────────────────────┘
```

**关键事实:Luanti 引擎支持命令行直接进服**,所以第一阶段 launcher 完全不用改引擎:

```sh
luanti --address play.example.com --port 30000 --name Steve --password "xxx" --go
```

新用户名首次连接即注册(服务器默认允许),密码就是注册密码 —— 这正是云端配给流程的落地点。

## 1. Launcher 项目结构建议(C#)

```
LuantiCN.Launcher/
├─ Api/CloudApi.cs        // HTTP 封装(全部 /api/cloud/client/*)
├─ Pairing/PairService.cs // luanticn:// 深链 + 配对
├─ Vault/JoinFlow.cs      // 进服状态机(核心)
├─ Presence/Heartbeat.cs  // 心跳线程
├─ Social/FriendsPanel.cs // 好友(30s 轮询)
├─ Social/PartyPanel.cs   // 组队(10s 轮询)
├─ Secure/TokenStore.cs   // DPAPI 存 token
└─ Process/ClientRunner.cs// 拉起/监听 luanti 进程
```

### CloudApi 骨架

```csharp
public class CloudApi
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://luanti.cn") };
    private string? _deviceToken;

    public void SetToken(string token) => _deviceToken = token;

    private async Task<JsonNode?> SendAsync(HttpMethod m, string path, object? body = null)
    {
        using var req = new HttpRequestMessage(m, path);
        if (_deviceToken is not null)
            req.Headers.Authorization = new("Bearer", _deviceToken);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var res = await _http.SendAsync(req);
        var json = JsonNode.Parse(await res.Content.ReadAsStringAsync());
        if (!res.IsSuccessStatusCode)
            throw new CloudApiException((int)res.StatusCode, json?["error"]?.GetValue<string>() ?? res.StatusCode.ToString());
        return json;
    }

    public Task<JsonNode?> PairAsync(string code, string deviceName) =>
        SendAsync(HttpMethod.Post, "/api/cloud/client/pair/", new { code, deviceName });

    public Task<JsonNode?> ProvisionAsync(string address, int? characterId) =>
        SendAsync(HttpMethod.Post, "/api/cloud/client/vault/provision/", new { address, characterId });

    public Task<JsonNode?> WritebackAsync(string address, string username, string password) =>
        SendAsync(HttpMethod.Put, "/api/cloud/client/vault/", new { address, username, password });

    public Task ReportInvalidAsync(string address) =>
        SendAsync(HttpMethod.Post, "/api/cloud/client/vault/invalid/", new { address });

    public Task NewPasswordAsync(int? length = null) => ...;   // GET /api/cloud/client/new-password/
    public Task<JsonNode?> FriendsAsync() => SendAsync(HttpMethod.Get, "/api/cloud/client/friends/");
    public Task HeartbeatAsync(string? address) => SendAsync(HttpMethod.Post, "/api/cloud/client/presence/", new { address });
    public Task<JsonNode?> PartyAsync() => SendAsync(HttpMethod.Get, "/api/cloud/client/party/");
    // party create/join/leave/end/server/kick 同理
}
```

## 2. 配对流程

### 2.1 注册 luanticn:// 协议(Windows)

```csharp
// 首次运行时写入(HKCU 不需要管理员)
using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\luanticn");
key.SetValue("URL Protocol", "");
using var cmd = key.CreateSubKey(@"shell\open\command");
cmd.SetValue("", $"\"{exePath}\" \"%1\"");
```

浏览器扫码/点深链 → 系统拉起 launcher,`args[0]` 形如 `luanticn://login?code=ABCD-EF23`,
用 `Uri` 解析出 `code`:

```csharp
var code = new Uri(args[0]).Query.Replace("?code=", "");
var res = await api.PairAsync(code, Environment.MachineName); // 一次性,10 分钟有效
TokenStore.Save((string)res["deviceToken"]);                  // 明文仅此一次!
```

### 2.2 Token 安全存储(DPAPI)

```csharp
public static class TokenStore
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LuantiCN", "device.bin");

    public static void Save(string token)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllBytes(Path, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
    }
    public static string? Load() => ...;   // 对应 Unprotect
}
```

启动时 `Load()` → `api.SetToken(...)` → `GET /api/cloud/client/me/` 校验;401 则引导重新配对。

## 3. 进服状态机(核心)

```csharp
public async Task JoinServerAsync(string address, int? characterId)
{
    // 1. 配给凭证(重复进同一服返回相同凭证;新服按角色策略生成)
    var cred = await api.ProvisionAsync(address, characterId);
    string username = (string)cred["username"];
    string password = (string)cred["password"];

    // 2. 拉起客户端
    var result = await ClientRunner.RunAsync(new {
        Exe = "luanti",
        Args = $"--address {host(address)} --port {port(address)} --name {username} --password {password} --go"
    });

    // 3. 失败分流(读退出输出 / debug.txt)
    if (result.FailedWith("Wrong password") || result.FailedWith("name"))   // 名字已被占/密码不符
    {
        var newName = PromptRename(defaultSuggestion: username);   // 弹窗让用户改名
        await api.WritebackAsync(address, newName, password);      // 回写云端
        await JoinServerAsync(address, characterId);               // 用新名字重试
        return;
    }

    // 4. 登录成功 → 心跳线程接管(见 §4);退出后上报 null
    await api.HeartbeatAsync(address);
    await WaitForExitAsync(result);
    await api.HeartbeatAsync(null);
}
```

**密码在别处被改**(原版客户端/另一台电脑)时,连接会报 `Wrong password` 但用户其实是老用户 ——
与"名字被占"不易区分时,统一处理:提示用户输入正确密码(或改名)→ `POST /vault/invalid/` 上报失效 →
`PUT /vault/` 回写 → 重试。两端语义云端都已支持,客户端只需按提示走。

## 4. 心跳线程

```csharp
public class Heartbeat : IDisposable
{
    private Timer? _timer;
    private readonly CloudApi _api;
    private string? _current;

    public void Start(string address)
    {
        _current = address;
        _timer = new Timer(async _ => {
            try { await _api.HeartbeatAsync(_current); } catch { /* 网络抖动忽略,下轮重试 */ }
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(60));
    }

    public async Task StopAsync()
    {
        _timer?.Dispose();
        try { await _api.HeartbeatAsync(null); } catch { }   // 退服上报,尽力而为
    }
}
```

节奏:**进服立即一次 + 每 60 秒一次 + 退服 null 一次**。停发超 5 分钟好友侧自动显示离线。

## 5. 好友 / 组队面板

- 好友:30 秒轮询 `GET /api/cloud/client/friends/`,列表含 `online` / `currentServerAddress`;
  「加入」按钮 = 复用 §3 的 `JoinServerAsync(friend.currentServerAddress)`。
- 组队:在房间时 10 秒轮询 `GET /api/cloud/client/party/`;
  `party.serverAddress` 变化且 ≠ 当前所在服 → 弹提示「队长切换了服务器,是否跟随」→ 加入;
  没在任何服时直接跟随目标服。

## 6. 游戏内改密的同步

改密对话框在引擎里,新密码引擎最先知道。三种方案按成本排序:

| 方案 | 改动 | 说明 |
|------|------|------|
| A. 本地文件接力(推荐) | 引擎 ~10 行 | 改密提交后把 `{address, username, password}` 写入 `<worlddir>/../luanticn_pw.json`;launcher 文件监视 → `PUT /vault/` → 删除文件 |
| B. 引擎直连云端 | 引擎较多 | 引擎把 device token 存进配置,改密后直接 POST;需在引擎里做 HTTP |
| C. 不做同步 | 无 | 靠"登录失败 → `POST /vault/invalid/` → 提示重输 → 回写"兜底,体验稍差但零改动 |

## 7. 服务器端上报 mod(完整示例)

服务器主安装 `luanticn_report` mod,`minetest.conf` 里两行配置 + 授权 HTTP:

```ini
secure.http_mods = luanticn_report
luanticn_report.token = ABCDEFGH23456789ABCDEFGH23456789
luanticn_report.address = play.example.com:30000
```

```lua
-- luanticn_report/init.lua
local http = minetest.request_http_api()
local token = minetest.settings:get("luanticn_report.token")
local address = minetest.settings:get("luanticn_report.address")
local endpoint = "https://luanti.cn/api/servers/report/"

if not (http and token and address) then
    minetest.log("warning", "[luanticn_report] 缺少 http 授权或配置,未启用上报")
    return
end

local function report()
    local payload = minetest.write_json({
        address = address,
        token = token,
        players_online = #minetest.get_connected_players(),
        players_max = 60,  -- 可按需读取设置
        motd = "",         -- 可选
    })
    http.fetch({
        url = endpoint,
        method = "POST",
        data = payload,
        extra_headers = { "Content-Type: application/json" },
        timeout = 10,
    }, function() end)  -- 上报失败静默,下轮重试
end

local timer = 0
minetest.register_globalstep(function(dtime)
    timer = timer + dtime
    if timer >= 60 then
        timer = 0
        report()
    end
end)

report()  -- 启动即报一次
```

10 分钟没有上报,服务器在大厅自动显示为离线。

## 8. 轮询 / 节奏速查

| 事项 | 时机 / 频率 |
|------|-------------|
| 心跳 presence | 进服立即 + 每 60s + 退服 null |
| 服务器上报 | 每 60s(服务端 mod) |
| 好友列表 | 30s |
| 组队状态 | 10s(在房间时) |
| 配对码 | 10 分钟有效,一次性 |
| Token 校验 | 启动时 `GET /client/me/`,401 → 重新配对 |

## 9. 安全清单

- [ ] device token 用 DPAPI/Keychain 加密存储,不进配置文件明文、不进 git
- [ ] 配对码一次性,配对失败(410)提示用户重新生成而不是重试
- [ ] `--password` 命令行会出现在进程列表里,介意可改用引擎补丁从 stdin/环境变量读(方案 B)
- [ ] 服务器上报 token 泄露时在网页「重新生成」立即作废旧 token
- [ ] 心跳/轮询失败一律静默重试,不要弹窗轰炸;退服上报尽力而为
