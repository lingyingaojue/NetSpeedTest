# Changelog

All notable changes to NetSpeedTest will be documented in this file.

---

## V1.3.4 (2026-07-27)

### 🐛 修复
- 修复 SaveResult JitterMs NOT NULL 约束崩溃（null→0 写、0→null 读）
- 修复外网延迟测速期间不刷新（12 主机批量 3s 超时 → 8.8.8.8 单主机 UDP 轮询）
- 修复抖动延迟不显示（ICMP Ping 满载超时 → 改为 TestGatewayLatencyAsync UDP 五层回退）
- 修复测速结束后弹窗延迟过长（移除 3 处冗余 finalLatency 阻塞调用 + 后台任务并行退出）
- 修复上传/双向测速 PeakMbps 始终为 0（PeakRate 跟踪从补偿门控拆出至无条件）
- 修复 GetStatistics 静默吞 DB 异常

### 🔧 优化
- WAN / 抖动 / LAN 延迟探测统一为 UDP 优先五层回退
- 延迟刷新频率默认 2000→1000ms，三指标同步
- 后台任务取消后并行 await + UDP 探针 CancellationToken 可打断
- 设置保存改为纯内存生效，不再持久化覆盖打包版默认值

### 📝 修正
- 设置页 "延迟轮询间隔"→"延迟采样间隔"

---

## V1.3.3 (2026-07-26)

### 🐛 修复
- 修复弹窗延迟与实时延迟不一致（弹窗误读 VM 属性而非 result 对象）
- 修复 LongRef 值拷贝导致平均速度始终为 0
- 修复 allRateSamples.Min() 空列表崩溃
- 修复 ProfileService INSERT OR REPLACE 覆盖原始 CreatedAt
- 修复 GetAllRecords int.MaxValue 全量加载 OOM
- 修复下载失败无退避 CPU 空转
- 修复 NIC 监控 fire-and-forget 竞态（改返 Task + await）
- 修复 SettingsWindow InputBackgroundBrush 未定义崩溃
- 修复 FormatLatency(0) 误显示 "--"
- 修复 CSV 导出 NaN 字符串 + SSRF 内网阻断
- 修复自适应 PeakEfficiency 只增不减（新增衰减）
- 修复 _stopwatch 跨线程可见性（volatile）
- 修复 EULA 撤销后退托盘不退出
- 修复日志消息颠倒 + 翻页恢复硬编码
- 修复多窗口低 DPI / 非全屏文字溢出

### 🔧 优化
- 延迟 / 抖动 / NIC 速率共用平均计量延迟设置
- 抖动算法重构：固定 8.8.8.8 单次 ICMP + 滑动窗口标准差
- WAN DNS 阻塞修复：删同步预解析，改异步动态解析
- 取消后弹窗显示部分结果并正常入库
- 滚动条美化 + 横向滚轮支持 + F2 精度 + 窗口屏幕自适应
- 补偿恢复阈值 0.8 → 0.5 + 退出重置 PeakRate

### 🚀 新增
- 抖动探测独立配置（主机 / 间隔滑块）
- 全窗口 TextTrimming / TextWrapping / ScrollViewer 溢出防护
- Logger.Log 诊断日志链

### 📝 修正
- HistoryWindow 列头 "延迟" → "平均延迟"
- 弹窗标签 "抖动延迟" → "平均抖动延迟"
- 设置页 "延迟轮询间隔" → "延迟采样间隔"

---

## V1.3.1 (2026-07-24)

### 🐛 修复
- 修复外网延迟显示异常（绑定属性未同步/批内多次回调拉高均/最终 LAN 延迟 Token 已取消）
- 修复设置页内容溢出遮挡（加滚动条 + 修正 Grid 行号越界）
- 修复历史记录 DataGrid 列重复（补 AutoGenerateColumns=False）
- 修复 AdaptiveThreadsEnabled 无法通过设置保存
- 修复撤销同意删除版本记录（改删键值而非整键）
- 修复补偿检测仅监控下载方向（补上传方向双向联合检测）

### 🔧 优化
- 新增应用图标（exe/任务栏/窗口标题栏/关于页）
- 历史 DataGrid 设为只读
- 错误提示文案修正

### 🚀 新增
- 关于页面显示应用图标

---

## V1.3.0 (2026-07-23)

### 🐛 修复
- 修复信号量槽泄漏（回调异常导致并发量永久下降）
- 修复全速测试自适应线程检测失效（tc 默认值误用）
- 修复全速测试补偿时长剔除缺失
- 修复 UI 与 DB 之间延迟值不一致（历史记录偏差）
- 修复 DB 写入静默失败（增加异常日志兜底）
- 修复 CancelTest 定时器处理器未摘除

### 🔧 优化
- 图表单测模式切换增加 300ms 平滑过渡动画
- 下载/上传测速互斥回调（不显示对方方向数值）
- 多网卡信息卡（9 项详情 + 彩色分类布局）
- 历史记录独立页面 + 一键清除全部
- 首页底部网络信息卡替代历史表格

### 🚀 新增
- 掉速紧急补偿（检测 + 自动加线程 + 最终结果修正）
- 自适应线程上限（低配电脑防止多线程反噬降速）

---

## V1.2.0 (2026-07-17)

### 🚀 新增
- 多线程上传测速 — 128 线程并发 HTTP POST（64KB 随机数据块），失败自动换 URL
- 全速双向测速 — 下载 + 上传同时进行，交替分配线程
- NIC 计数器级速率 — 基于系统网卡 `IPv4Statistics` 差分计算，不受 HTTP 开销干扰
- 自定义测速配置 — 新建/删除/重命名，独立管理下载/上传 URL
- 配置导入/导出 — JSON 格式（兼容 HBCS），跨设备迁移
- 历史记录 — SQLite 持久化，主窗口最近 20 条，独立历史窗口分页查看
- 数据导出 — 最新测速结果导出 JSON
- 实时图表 — LiveCharts2 下载/上传速率折线图，可拖拽分割线
- 内网延迟检测 — ICMP → TCP 443 → HTTPS HEAD → HTTP HEAD 四层回退
- 首次启动 EULA — 同意协议方可使用
- 更新日志弹窗 — 按版本号检测，首次运行自动弹出
- 暗色主题 — GitHub Dark 风格

### 🔧 优化
- 8 个内置 CDN 节点（阿里云×2、腾讯云×2、华为云、电信、联通、移动）
- 3 秒滑动窗口平滑速率显示
- 线程渐变启动（可配间隔，默认 200ms）
- 全局超时控制 `CancellationTokenSource` 统一管理
- 每网卡速率条实时显示
- 速率 < 1 Mbps → Kbps，≥ 1000 Mbps → Gbps，全数值 InvariantCulture 格式化
- 设置面板 6 项可调参数

---

## V1.1.1 (2026-07-14)

### 🚀 新增
- 首个正式发布版本
- 多线程下载测速（HTTP GET，多 URL 轮询）
- 外网延迟检测（12 个公网目标并发 Ping）
- 基础 MVVM 架构（.NET 8 + WPF + CommunityToolkit.Mvvm）
