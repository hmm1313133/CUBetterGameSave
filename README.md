# Better Game Save

**Casualties: Unknown - Better Game Save Mod**

 更强大的游戏存档管理模组，提供存档容错加载、KrokMP 联机支持，以及面向其他 mod 作者的开放 API。

## 功能概览

### 核心功能

| 功能 | 说明 |
|------|------|
| 存档容错加载 | 游戏存档损坏时自动尝试恢复，避免直接崩溃 |
| 存档槽管理 | 浏览、加载、删除、备份存档槽 |
| 错误诊断 | 存档加载失败时输出详细诊断报告 |
| 自动备份 | 损坏存档自动备份，不丢失原始数据 |
| 多人联机支持 | 完整兼容 KrokMP 联机模组 |
| 开放 API | 提供标准接口让其他 mod 接入存档系统 |


## 开放 API

其他 mod 作者可以通过 `BetterSaveApi` 接入本模组的存档系统。

### 快速接入

```csharp
using BetterGameSave.API;

[BepInPlugin("com.yourmod.guid", "Your Mod", "1.0.0")]
[BepInDependency("com.cu.bettergamesave", BepInDependency.DependencyFlags.SoftDependency)]
public class YourPlugin : BaseUnityPlugin
{
    public void Awake()
    {
        // 检查 BetterGameSave 是否已安装
        if (BetterSaveApi.IsAvailable && BetterSaveApi.Api is { } api)
        {
            // 注册自定义存档数据提供者
            api.RegisterSaveDataProvider("com.yourmod.guid", new YourSaveDataProvider());

            // 订阅存档/加载事件
            api.OnPreSave += OnPreSave;
            api.OnPostLoad += OnPostLoad;
        }
    }

    private void OnPreSave(int slotIndex)
    {
        // 存档前的准备工作
    }

    private void OnPostLoad(int slotIndex)
    {
        // 加载后的初始化工作
    }
}

// 实现 ISaveDataProvider 接口
public class YourSaveDataProvider : ISaveDataProvider
{
    public IReadOnlyDictionary<string, object> OnSave()
    {
        return new Dictionary<string, object>
        {
            ["your_data"] = YourModState.Serialize(),
        };
    }

    public void OnLoad(IReadOnlyDictionary<string, object> data)
    {
        if (data.TryGetValue("your_data", out var value))
            YourModState.Deserialize(value);
    }

    public void OnSlotDeleted(int slotIndex)
    {
        // 清理与该存档槽关联的数据
    }
}
```

### API 接口

| 方法/属性 | 说明 |
|-----------|------|
| `RegisterSaveDataProvider(modId, provider)` | 注册自定义存档数据提供者 |
| `UnregisterSaveDataProvider(modId)` | 取消注册 |
| `GetCurrentSlotInfo()` | 获取当前存档槽信息 |
| `GetAllSlots()` | 获取所有可用存档槽列表 |
| `RequestSave()` | 手动触发存档 |
| `RequestLoad(slotIndex)` | 手动触发加载 |
| `IsMultiplayerActive` | 检查是否在联机模式 |
| `OnPreSave` / `OnPostSave` | 存档前/后事件 |
| `OnPreLoad` / `OnPostLoad` | 加载前/后事件 |

## 项目结构

```
BetterGameSave/
├── BetterGameSave.sln               # Visual Studio 解决方案
├── vars.targets                      # 构建变量（游戏路径等）
├── README.md                         # 本文件
├── LICENSE
├── Release/                          # 发布产物
├── scripts/                          # 构建脚本
│   └── package_release.py
└── BetterGameSave/                   # 主项目
    ├── BetterGameSave.csproj          # 项目文件
    ├── Plugin.cs                      # BepInEx 插件入口
    ├── API/                           # 开放 API
    │   ├── IBetterSaveApi.cs          # API 接口定义
    │   ├── ISaveDataProvider.cs       # 存档数据提供者接口
    │   └── BetterSaveApi.cs           # API 静态访问器
    ├── Core/                          # 核心逻辑
    │   ├── SaveManager.cs             # 存档管理器
    │   └── SaveSerializer.cs          # 序列化工具
    ├── Compatibility/                # 兼容性层
    │   ├── KrokMpCompat.cs            # KrokMP 联机兼容
    │   ├── QolCompat.cs              # QoL Unknown 兼容
    │   └── ModCompatRegistry.cs      # 已知 mod 注册表
    ├── Patches/                       # Harmony 补丁
    │   ├── SaveLoadPatches.cs         # 存档/加载补丁
    │   └── ErrorHandlingPatches.cs    # 错误处理补丁
    ├── UI/                            # 用户界面
    │   └── SaveMenuUI.cs             # 存档管理菜单
    ├── Util/                          # 工具类
    │   └── Logger.cs                  # 日志工具
    └── Lang/                          # 多语言
        ├── EN.json                    # 英文
        └── zh_CN.json                 # 简体中文
```

## 技术栈

- **运行时**: .NET Framework 4.8
- **框架**: BepInEx 5
- **游戏**: Casualties: Unknown
- **补丁**: HarmonyX (via BepInEx)
- **序列化**: Newtonsoft.Json
- **程序集公开化**: BepInEx.AssemblyPublicizer

## 构建

### 前置条件

- Visual Studio 2022 或 .NET SDK (支持 net48)
- 游戏已安装 BepInEx 5

### 配置游戏路径

编辑 `vars.targets`，将 `BaseGamePath` 指向你的游戏安装目录：

```xml
<BaseGamePath>O:/SteamLibrary/steamapps/common/Casualties Unknown Demo</BaseGamePath>
```

### 编译

```bash
dotnet build BetterGameSave.sln -c Release
```

构建后 DLL 会自动复制到 `BepInEx/plugins/BetterGameSave/` 目录。

## 模组信息

| 属性 | 值 |
|------|-----|
| GUID | `com.cu.bettergamesave` |
| 名称 | Casualties: Unknown - Better Game Save |
| 版本 | 0.1.0 |
| 软依赖 | QoL Unknown, KrokMP |

## 开发计划

- [ ] 存档/加载 Harmony 补丁实现
- [ ] 存档容错恢复机制
- [ ] 存档管理 UI 实现
- [ ] 错误诊断报告系统
- [ ] 存档槽备份与恢复
- [ ] CUCoreLib 集成（可选）

## 许可证

MIT License
