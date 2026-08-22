/* ============================================================
   NetSpeedTest — 官方网站交互脚本（无第三方依赖）
   ============================================================ */

(function () {
  "use strict";

  /* 脚本加载成功后再启用“滚动显现”，避免脚本失败时页面空白 */
  document.documentElement.classList.add("js");

  /* ---------- 主题切换 & 中英文切换 ---------- */
  var SITE_I18N_EN = {
    "跳到主要内容": "Skip to content",
    "Windows 网络测速": "Windows network speed test",
    "核心特性": "Features",
    "界面预览": "Screenshots",
    "工具箱": "Toolbox",
    "技术架构": "Architecture",
    "更新日志": "Changelog",
    "免费下载": "Download",
    "v1.4.0 已发布 · 多网卡并行测速": "v1.4.0 released · Multi-NIC parallel testing",
    "把 Windows 网络测速": "Test Windows networks",
    "专业级": "like a pro",
    "做到": " ",
    "CDN 智能调度 · 自适应线程引擎 · 多网卡并行测速 · 掉速紧急补偿 · UDP 五层延迟探测 ·": "CDN smart routing · Adaptive threading · Multi-NIC parallel · Drop compensation · UDP-first latency probing",
    "18 合 1 工具箱，一款工具全部搞定。": "18-in-1 toolbox. One app covers it all.",
    "下载 Windows 版": "Download for Windows",
    "GitHub 仓库": "GitHub repo",
    "Windows 10 / 11 · .NET 8 单文件发布 · MIT 开源免费": "Windows 10 / 11 · .NET 8 single-file · MIT open source",
    "实时测速演示": "Live demo",
    "下载": "Download",
    "上传": "Upload",
    "总速率": "Total",
    "均值参考线": "Avg reference",
    "线程": "threads",
    "层": "layers",
    "种": "tools",
    "个": "nodes",
    "并发下载 / 上传": "Concurrent download / upload",
    "UDP 优先延迟回退": "UDP-first latency fallback",
    "网络诊断工具": "Network diagnostic tools",
    "内置 CDN 节点": "Built-in CDN nodes",
    "为重度网络用户设计的引擎": "An engine built for power users",
    "不只是跑个数字——从线程调度到结果计量，每一层都按专业标准实现。": "More than a number — thread scheduling and metering are engineered to professional standards.",
    "智能测速引擎": "Smart speed engine",
    "128 线程并发 HTTP GET / POST": "128-thread concurrent HTTP GET / POST",
    "URL 动态调度，实时选择最优节点": "Dynamic URL scheduling picks the fastest node",
    "CPU 自适应线程上限，低配不反噬": "CPU-adaptive thread cap protects low-end PCs",
    "掉速紧急补偿：自动加线程 + 结果修正": "Drop compensation: auto add threads + fix results",
    "10s 稳定后取均值，3s 滑动窗口去毛刺": "Averages after 10s stability; 3s smoothing window",
    "200ms 渐变启动，避免瞬间占满带宽": "200ms ramp-up avoids instant bandwidth saturation",
    "多网卡并行测速": "Multi-NIC parallel testing",
    "勾选多张网卡同时测速，独立绑定源 IP": "Check multiple NICs to test simultaneously with bound source IPs",
    "下载 / 上传曲线支持「合计 / 单网卡」切换": "Download / upload charts switch between Total and per-NIC",
    "双向测速显示下载 + 上传总速度": "Duplex mode shows download + upload total speed",
    "完成弹窗展示每网卡独立速率与错误": "Result dialog shows each NIC rate and error",
    "默认优先选中默认网关网卡": "Default-gateway NIC selected by default",
    "NIC 级精准计量": "NIC-level precision metering",
    "基于": "Based on",
    "差分计算": "differential counters",
    "不受 HTTP 协议开销干扰": "No HTTP overhead interference",
    "Kbps / Mbps / Gbps 自动切换": "Kbps / Mbps / Gbps auto-scaling",
    "每网卡 9 项详情信息卡": "9-detail info card per NIC",
    "独立速率条 + 活跃线程计数": "Independent rate bar + active thread count",
    "专业交互体验": "Professional UX",
    "LiveCharts2 双折线图，200ms 采样、500 点窗口": "LiveCharts2 dual line charts, 200ms sampling, 500-point window",
    "下载 / 上传图表可拖拽分割线": "Draggable splitter between download / upload charts",
    "模式切换 300ms 平滑过渡动画": "300ms smooth mode transitions",
    "系统托盘驻留 + 气泡通知": "System tray + toast notifications",
    "GitHub Dark 暗色主题、自绘标题栏": "GitHub Dark theme, custom title bar",
    "内嵌页面架构，告别弹窗": "Embedded pages instead of popups",
    "全链路 UDP 优先延迟探测": "UDP-first latency probing",
    "五层回退：UDP → ICMP → TCP 443 → HTTPS HEAD → HTTP HEAD": "5-layer fallback: UDP → ICMP → TCP 443 → HTTPS HEAD → HTTP HEAD",
    "WAN / 抖动 / LAN 共用同一探测链路": "WAN / jitter / LAN share one probing chain",
    "8.8.8.8 单主机 UDP 轮询，避免批量阻塞": "8.8.8.8 single-host UDP polling avoids blocking",
    "滑动窗口标准差算法，抖动实时平滑输出": "Sliding-window stddev for smooth jitter output",
    "1000ms 刷新，三指标同步更新": "1000ms refresh, three metrics in sync",
    "历史 & 配置管理": "History & profiles",
    "SQLite 持久化，独立页面 + 统计栏": "SQLite persistence with stats bar",
    "历史记录 CSV 导出、一键清除": "CSV export and one-click clear",
    "8 个内置 CDN 节点": "8 built-in CDN nodes",
    "自定义配置导入 / 导出（JSON 兼容 HBCS）": "Custom profile import / export (JSON, HBCS-compatible)",
    "设置纯内存生效，不污染打包版默认值": "In-memory settings keep defaults clean",
    "暗色主题 · 侧边栏导航 · 自绘标题栏": "Dark theme · Sidebar navigation · Custom title bar",
    "v1.4.0 主界面：选择测速模式，勾选要同时测速的网卡，一键开始。": "v1.4.0 UI: choose a mode, check NICs, start with one click.",
    "🖥️ v1.4.0 主界面": "🖥️ v1.4.0 main UI",
    "截图由用户实机提供": "Screenshot provided by a real user",
    "网络工具箱": "Network toolbox",
    "18 合 1，一窗口搞定日常排查": "18 tools in one window",
    "从连通性测试到 NAT 类型检测，测速之外的网络诊断需求也全部内置。": "From connectivity tests to NAT detection, diagnostics are built in.",
    "Ping": "Ping",
    "ICMP 连通性测试": "ICMP connectivity test",
    "DNS 查询": "DNS lookup",
    "A / AAAA / CNAME / MX 记录": "A / AAAA / CNAME / MX records",
    "HTTP 请求": "HTTP request",
    "GET / POST / PUT / DELETE": "GET / POST / PUT / DELETE",
    "路由追踪": "Traceroute",
    "Traceroute 跳点追踪": "Hop-by-hop route tracing",
    "端口测试": "Port test",
    "TCP 端口范围扫描": "TCP port range scan",
    "MTU 探测": "MTU probe",
    "自动发现路径最优值": "Auto-discover path MTU",
    "DNS 对比": "DNS compare",
    "多 DNS 服务器并行对比": "Compare multiple DNS servers",
    "IP 归属": "IP info",
    "IP 地理 / 运营商查询": "IP geo / ISP lookup",
    "公网 IP": "Public IP",
    "多源探测公网出口 IP": "Multi-source public IP detection",
    "SSL 证书": "SSL certificate",
    "证书链 / 过期时间 / 签名算法": "Chain / expiry / signature",
    "HTTP Header": "HTTP headers",
    "任意 URL 响应头查看": "Inspect any URL response headers",
    "子网计算": "Subnet calc",
    "CIDR 划分 / 可用地址": "CIDR / usable addresses",
    "带宽换算": "Bandwidth converter",
    "Mbps / MBps / Gbps 换算": "Mbps / MBps / Gbps conversion",
    "时间戳": "Timestamp",
    "Unix 时间戳 ↔ 日期": "Unix timestamp ↔ date",
    "文本哈希": "Hash",
    "MD5 / SHA1 / SHA256 / SHA512": "MD5 / SHA1 / SHA256 / SHA512",
    "Base64": "Base64",
    "在线编解码": "Online encode / decode",
    "UUID 生成": "UUID generator",
    "UUID v4 / v7 批量生成": "UUID v4 / v7 batch",
    "NAT 检测": "NAT detection",
    "STUN 类型 / 自定义服务器": "STUN type / custom server",
    "效率": "Efficiency",
    "快捷键": "Shortcuts",
    "一只手开始测速，另一只手继续喝咖啡。": "Start a test with one hand, keep coffee in the other.",
    "开始测速": "Start",
    "停止测速": "Stop",
    "仅下载": "Download only",
    "仅上传": "Upload only",
    "全速双向": "Full duplex",
    "可控": "Control",
    "可调参数": "Tunable parameters",
    "默认值已经够快，细粒度交给喜欢折腾的人。": "Defaults are fast; fine-tuning is for tinkerers.",
    "参数": "Parameter",
    "范围": "Range",
    "默认": "Default",
    "并发线程数": "Threads",
    "整体超时": "Timeout",
    "线程启动间隔": "Ramp-up interval",
    "平均计量延迟": "Averaging delay",
    "速率平滑窗口": "Rate window",
    "网卡轮询间隔": "NIC poll interval",
    "抖动探测主机": "Jitter host",
    "抖动采样间隔": "Jitter interval",
    "干净、现代、可扩展": "Clean, modern, scalable",
    "基于 .NET 8 与 WPF 的 MVVM 架构，依赖项克制而清晰。": "MVVM on .NET 8 and WPF with a lean dependency set.",
    "运行时": "Runtime",
    ".NET 8.0-windows": ".NET 8.0-windows",
    "单文件发布": "Single-file publish",
    "UI 框架": "UI framework",
    "WPF": "WPF",
    "Windows 原生体验": "Native Windows experience",
    "架构模式": "Architecture",
    "MVVM": "MVVM",
    "CommunityToolkit.Mvvm": "CommunityToolkit.Mvvm",
    "实时图表": "Charts",
    "LiveCharts2": "LiveCharts2",
    "SkiaSharp 硬件渲染": "SkiaSharp hardware rendering",
    "数据持久化": "Persistence",
    "SQLite": "SQLite",
    "Microsoft.Data.Sqlite": "Microsoft.Data.Sqlite",
    "DI & 配置": "DI & config",
    "Microsoft.Extensions": "Microsoft.Extensions",
    "标准配置管线": "Standard configuration pipeline",
    "持续迭代": "Continuously evolving",
    "每个版本都经过实机测速验证，拒绝空壳功能。": "Every release is verified on real machines.",
    "多网卡并行测速": "Multi-NIC parallel testing",
    "界面全面重构": "UI overhaul",
    "18 合 1 工具箱": "18-in-1 toolbox",
    "🚀 新功能": "🚀 New",
    "🛠️ 修复 & 优化": "🛠️ Fixes & improvements",
    "🚀 重大更新": "🚀 Major update",
    "🛠️ 修复": "🛠️ Fixes",
    "完整变更记录请查看": "See the full changelog",
    "立即体验": "Get started",
    "下载 NetSpeedTest": "Download NetSpeedTest",
    "Windows 10 / 11 · .NET 8 单文件发布 · 约 170 MB · MIT 开源免费": "Windows 10 / 11 · .NET 8 single-file · ~170 MB · MIT",
    "前往 Releases 下载": "Go to Releases",
    "浏览源代码": "View source",
    "或者从源码构建：": "Or build from source:",
    "Windows 桌面端网络测速工具 · 专业级 · 开源免费": "Windows desktop speed test · Professional · Open source",
    "均值 ": "Avg ",
    "联系方式": "Contact",
    "找到我们": "Get in touch",
    "欢迎反馈问题、交流使用经验或合作。": "Feedback, tips and cooperation are welcome.",
    "邮箱": "Email",
    "微信好友": "WeChat",
    "QQ好友": "QQ friend",
    "QQ交流群": "QQ group",
    "扫码添加": "Scan to add",
    "扫码加入": "Scan to join",
    "复制邮箱": "Copy email",
    "打开": "Open",
    "保存": "Save",
    "复制成功": "Copied!",
    "复制失败": "Copy failed",
    "v1.4.1 已发布 · 关于页改版与联系方式": "v1.4.1 released · New About page & contacts",
    "v1.4.1 主界面：选择测速模式，勾选要同时测速的网卡，一键开始。": "v1.4.1 UI: choose a mode, check NICs, start with one click.",
    "🖥️ v1.4.1 主界面": "🖥️ v1.4.1 main UI",
    "自适应线程调度：线性加压，最高 1024 线程": "Adaptive threading: linear ramp-up to 1024 threads",
    "丢包率实时监测，结果 / 历史 / CSV / Web API 全链路记录": "Live packet loss tracking across results, history, CSV and Web API",
    "内置 Web 服务器 + Web 控制台，REST API 远程测速": "Built-in Web server + Web console with REST API remote testing",
    "关于页改版与联系方式": "About page redesign & contacts",
    "关于页改版：开发者 / AI 协作 / GitHub / 官方网站四张信息卡，官网与 GitHub 可点击跳转": "Redesigned About page: developer, AI, GitHub and website cards with clickable links",
    "联系方式点击复制：邮箱、微信、QQ 一键复制到剪贴板": "One-click copy for email, WeChat and QQ",
    "复制成功弹窗：「已复制」提示，2 秒自动关闭": "Copied toast with 2-second auto close",
    "复制流程更稳定：剪贴板被占用时提示不受影响": "More reliable copy flow when the clipboard is busy",
    "✨ 优化": "✨ Improvements",
    "中文": "中文",
    "English": "English"
  };

  var siteLang = localStorage.getItem("nst-site-lang") || "zh";
  var siteTheme = localStorage.getItem("nst-site-theme") || "dark";
  var siteTextNodes = [];
  var siteTextCaptured = false;

  function siteT(text) {
    if (siteLang === "en" && SITE_I18N_EN[text]) return SITE_I18N_EN[text];
    return text;
  }

  function captureSiteText() {
    if (siteTextCaptured) return;
    siteTextCaptured = true;
    var walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, null);
    var node;
    while ((node = walker.nextNode())) {
      if (node.nodeValue && node.nodeValue.trim() && node.parentElement && node.parentElement.tagName !== "SCRIPT") {
        siteTextNodes.push({ node: node, original: node.nodeValue });
      }
    }
  }

  function applySiteLanguage(lang) {
    siteLang = lang;
    localStorage.setItem("nst-site-lang", lang);
    captureSiteText();
    siteTextNodes.forEach(function (item) {
      var value = item.original;
      if (lang === "en") {
        Object.keys(SITE_I18N_EN).sort(function (a, b) { return b.length - a.length; }).forEach(function (key) {
          value = value.split(key).join(SITE_I18N_EN[key]);
        });
      }
      item.node.nodeValue = value;
    });
    document.documentElement.lang = lang === "en" ? "en" : "zh-CN";
    var sel = document.getElementById("siteLangSwitch");
    if (sel) sel.value = lang;
    window.dispatchEvent(new Event("resize"));
  }

  function applySiteTheme(theme) {
    siteTheme = theme;
    localStorage.setItem("nst-site-theme", theme);
    document.documentElement.classList.toggle("light", theme === "light");
    var btn = document.getElementById("siteThemeToggle");
    if (btn) btn.textContent = theme === "light" ? "☀️" : "🌙";
  }

  var siteThemeBtn = document.getElementById("siteThemeToggle");
  var siteLangSelect = document.getElementById("siteLangSwitch");
  if (siteThemeBtn) {
    siteThemeBtn.addEventListener("click", function () {
      applySiteTheme(siteTheme === "light" ? "dark" : "light");
    });
  }
  if (siteLangSelect) {
    siteLangSelect.addEventListener("change", function () {
      applySiteLanguage(siteLangSelect.value === "en" ? "en" : "zh");
    });
  }

  var copyEmailBtn = document.getElementById("copyEmailBtn");
  if (copyEmailBtn) {
    copyEmailBtn.addEventListener("click", function () {
      var email = "mashuo2010az@163.com";
      function done() {
        copyEmailBtn.textContent = siteT("复制成功");
        setTimeout(function () { copyEmailBtn.textContent = siteT("复制邮箱"); }, 2000);
      }
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(email).then(done, function () {
          fallbackCopy();
        });
      } else {
        fallbackCopy();
      }
      function fallbackCopy() {
        var ta = document.createElement("textarea");
        ta.value = email;
        ta.style.position = "fixed";
        ta.style.opacity = "0";
        document.body.appendChild(ta);
        ta.select();
        try {
          if (document.execCommand("copy")) done();
          else copyEmailBtn.textContent = siteT("复制失败");
        } catch (e) {
          copyEmailBtn.textContent = siteT("复制失败");
        }
        document.body.removeChild(ta);
      }
    });
  }

  applySiteTheme(siteTheme);
  applySiteLanguage(siteLang);

  /* ---------- 顶部导航：滚动阴影 & 移动端菜单 ---------- */
  const header = document.getElementById("siteHeader");
  const navToggle = document.getElementById("navToggle");
  const siteNav = document.getElementById("siteNav");

  function onScroll() {
    if (!header) {
      return;
    }
    header.classList.toggle("scrolled", window.scrollY > 8);
  }

  window.addEventListener("scroll", onScroll, { passive: true });
  onScroll();

  if (navToggle && siteNav) {
    navToggle.addEventListener("click", function () {
      const open = siteNav.classList.toggle("open");
      navToggle.classList.toggle("active", open);
      navToggle.setAttribute("aria-expanded", open ? "true" : "false");
      navToggle.setAttribute("aria-label", open ? "关闭导航菜单" : "打开导航菜单");
    });

    siteNav.querySelectorAll("a").forEach(function (link) {
      link.addEventListener("click", function () {
        siteNav.classList.remove("open");
        navToggle.classList.remove("active");
        navToggle.setAttribute("aria-expanded", "false");
        navToggle.setAttribute("aria-label", "打开导航菜单");
      });
    });
  }

  /* ---------- 菜单滑动指示器：点击 / 滚动联动 ---------- */
  const navIndicator = document.getElementById("navIndicator");
  const navMedia = window.matchMedia ? window.matchMedia("(max-width: 960px)") : null;

  if (siteNav && navIndicator) {
    const navLinks = Array.prototype.slice.call(siteNav.querySelectorAll('a[href^="#"]'));
    const sectionIds = navLinks
      .map(function (link) {
        return link.getAttribute("href").slice(1);
      })
      .filter(function (id) {
        return document.getElementById(id);
      });
    let activeNavLink = null;
    let scrollSpyLocked = false;
    let scrollEndTimer = null;

    function isMobileNav() {
      return navMedia ? navMedia.matches : false;
    }

    function positionIndicator(link, instant) {
      if (instant) {
        navIndicator.style.transition = "none";
      }

      const navRect = siteNav.getBoundingClientRect();
      const linkRect = link.getBoundingClientRect();
      const navStyle = window.getComputedStyle(siteNav);
      const borderLeft = parseFloat(navStyle.borderLeftWidth) || 0;
      const borderTop = parseFloat(navStyle.borderTopWidth) || 0;

      if (isMobileNav()) {
        const indicatorTop = parseFloat(getComputedStyle(navIndicator).top) || 0;
        navIndicator.style.width = "";
        navIndicator.style.height = linkRect.height + "px";
        navIndicator.style.transform =
          "translate3d(0, " + (linkRect.top - navRect.top - borderTop - indicatorTop) + "px, 0)";
      } else {
        navIndicator.style.width = linkRect.width + "px";
        navIndicator.style.height = "";
        navIndicator.style.transform =
          "translate3d(" + (linkRect.left - navRect.left - borderLeft) + "px, 0, 0)";
      }

      navIndicator.style.opacity = "1";

      if (instant) {
        void navIndicator.offsetWidth;
        navIndicator.style.transition = "";
      }
    }

    function setActiveLink(link, force) {
      if (!link) {
        return;
      }
      if (activeNavLink === link && !force) {
        return;
      }
      activeNavLink = link;
      navLinks.forEach(function (item) {
        item.classList.toggle("active", item === link);
      });
      positionIndicator(link, force);
    }

    function refreshIndicator() {
      if (activeNavLink) {
        positionIndicator(activeNavLink, true);
      }
    }

    navLinks.forEach(function (link) {
      link.addEventListener("click", function () {
        scrollSpyLocked = true;
        if (scrollEndTimer) {
          clearTimeout(scrollEndTimer);
          scrollEndTimer = null;
        }
        setActiveLink(link);
      });
    });

    function updateActiveByScroll(instant) {
      if (!sectionIds.length) {
        return;
      }

      const probe = window.scrollY + 130;
      const atBottom = window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 4;
      let currentId = sectionIds[0];

      sectionIds.forEach(function (id) {
        const section = document.getElementById(id);
        if (section) {
          const sectionTop = section.getBoundingClientRect().top + window.scrollY;
          if (sectionTop <= probe) {
            currentId = id;
          }
        }
      });

      if (atBottom) {
        currentId = sectionIds[sectionIds.length - 1];
      }

      const target = navLinks.find(function (link) {
        return link.getAttribute("href") === "#" + currentId;
      });

      if (target) {
        setActiveLink(target, instant);
      }
    }

    function handleScrollSpy() {
      if (!scrollSpyLocked) {
        updateActiveByScroll();
      }

      if (scrollEndTimer) {
        clearTimeout(scrollEndTimer);
      }

      scrollEndTimer = setTimeout(function () {
        scrollEndTimer = null;
        if (scrollSpyLocked) {
          scrollSpyLocked = false;
          updateActiveByScroll();
        }
      }, 160);
    }

    function initialActiveLink() {
      const hash = window.location.hash;

      if (hash) {
        const matched = navLinks.find(function (link) {
          return link.getAttribute("href") === hash;
        });
        if (matched) {
          setActiveLink(matched, true);
          return;
        }
      }

      updateActiveByScroll(true);
    }

    initialActiveLink();
    window.addEventListener("scroll", handleScrollSpy, { passive: true });
    window.addEventListener("resize", refreshIndicator);
    window.addEventListener("load", refreshIndicator);
  }

  /* ---------- 鼠标交互光效 ---------- */
  const cursorGlow = document.getElementById("cursorGlow");
  const reducedMotion = window.matchMedia ? window.matchMedia("(prefers-reduced-motion: reduce)").matches : false;
  const finePointer = window.matchMedia ? window.matchMedia("(hover: hover) and (pointer: fine)").matches : true;

  if (cursorGlow && !reducedMotion && finePointer) {
    let targetX = window.innerWidth / 2;
    let targetY = window.innerHeight / 2;
    let glowX = targetX;
    let glowY = targetY;
    let glowRafId = 0;

    function renderGlow() {
      const dx = targetX - glowX;
      const dy = targetY - glowY;
      glowX += dx * 0.14;
      glowY += dy * 0.14;
      cursorGlow.style.transform =
        "translate3d(" + glowX + "px, " + glowY + "px, 0) translate(-50%, -50%)";

      if (Math.abs(dx) > 0.15 || Math.abs(dy) > 0.15) {
        glowRafId = requestAnimationFrame(renderGlow);
      } else {
        glowRafId = 0;
      }
    }

    function wakeGlow() {
      if (!glowRafId) {
        glowRafId = requestAnimationFrame(renderGlow);
      }
    }

    window.addEventListener(
      "mousemove",
      function (event) {
        targetX = event.clientX;
        targetY = event.clientY;
        wakeGlow();
      },
      { passive: true }
    );

    document.documentElement.addEventListener("mouseleave", function () {
      targetX = -320;
      targetY = -320;
      wakeGlow();
    });

    wakeGlow();
  }

  const glowCards = document.querySelectorAll(
    ".feature-card, .tool-item, .stack-item, .screenshot-frame, .demo-card, .download-box"
  );

  if (!reducedMotion && "matchMedia" in window && window.matchMedia("(hover: hover)").matches) {
    glowCards.forEach(function (card) {
      card.classList.add("glow-card");

      card.addEventListener("mousemove", function (event) {
        const rect = card.getBoundingClientRect();
        card.style.setProperty("--mx", (event.clientX - rect.left) + "px");
        card.style.setProperty("--my", (event.clientY - rect.top) + "px");
      });

      /* 鼠标离开时保留最后位置，避免光斑跳回中心闪一下 */
    });
  }

  /* ---------- 数字滚动 ---------- */
  function animateCounter(el) {
    const target = parseInt(el.dataset.target, 10) || 0;
    const duration = 1100;
    const start = performance.now();

    function step(now) {
      const progress = Math.min((now - start) / duration, 1);
      const eased = 1 - Math.pow(1 - progress, 3);
      el.textContent = String(Math.round(target * eased));
      if (progress < 1) {
        requestAnimationFrame(step);
      }
    }

    requestAnimationFrame(step);
  }

  /* ---------- 滚动显现 ---------- */
  const revealItems = document.querySelectorAll(".reveal");
  if ("IntersectionObserver" in window) {
    const revealObserver = new IntersectionObserver(
      function (entries, observer) {
        entries.forEach(function (entry) {
          if (entry.isIntersecting) {
            entry.target.classList.add("visible");
            observer.unobserve(entry.target);
          }
        });
      },
      { threshold: 0.12, rootMargin: "0px 0px -40px 0px" }
    );

    revealItems.forEach(function (el) {
      revealObserver.observe(el);
    });

    const counterObserver = new IntersectionObserver(
      function (entries, observer) {
        entries.forEach(function (entry) {
          if (entry.isIntersecting) {
            animateCounter(entry.target);
            observer.unobserve(entry.target);
          }
        });
      },
      { threshold: 0.6 }
    );

    document.querySelectorAll(".counter").forEach(function (el) {
      counterObserver.observe(el);
    });
  } else {
    revealItems.forEach(function (el) {
      el.classList.add("visible");
    });
    document.querySelectorAll(".counter").forEach(function (el) {
      el.textContent = el.dataset.target || "0";
    });
  }

  /* ---------- 实时测速演示波形 ---------- */
  const canvas = document.getElementById("speedChart");
  const demoDownload = document.getElementById("demoDownload");
  const demoUpload = document.getElementById("demoUpload");
  const demoTotal = document.getElementById("demoTotal");
  const demoElapsed = document.getElementById("demoElapsed");

  if (canvas && canvas.getContext) {
    const ctx = canvas.getContext("2d");
    const MAX_POINTS = 90;
    let pointsDownload = [];
    let pointsUpload = [];
    let width = 0;
    let height = 0;
    let dpr = 1;
    let lastPush = 0;
    let elapsedSeconds = 0;
    let startTime = 0;
    let downBase = 470;
    let upBase = 118;

    function randomBetween(min, max) {
      return min + Math.random() * (max - min);
    }

    function clamp(value, min, max) {
      return Math.min(Math.max(value, min), max);
    }

    function nextPoint(previous, base, volatility, min, max) {
      const drift = randomBetween(-8, 8);
      const impulse = randomBetween(-volatility, volatility);
      return clamp(previous * 0.82 + base * 0.18 + drift + impulse, min, max);
    }

    function seedSeries(base, volatility, min, max) {
      const series = [];
      let value = base * 0.72;
      for (let i = 0; i < MAX_POINTS; i += 1) {
        value = nextPoint(value, base, volatility, min, max);
        series.push(value);
      }
      return series;
    }

    pointsDownload = seedSeries(downBase, 42, 240, 680);
    pointsUpload = seedSeries(upBase, 14, 52, 210);

    function resize() {
      const rect = canvas.getBoundingClientRect();
      const cssWidth = rect.width > 0 ? rect.width : canvas.parentElement.clientWidth;
      const cssHeight = rect.height > 0 ? rect.height : 210;
      dpr = Math.max(window.devicePixelRatio || 1, 1);
      width = Math.max(Math.round(cssWidth), 10);
      height = Math.max(Math.round(cssHeight), 10);
      canvas.width = Math.round(width * dpr);
      canvas.height = Math.round(height * dpr);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }

    function niceMax(value) {
      const magnitude = Math.pow(10, Math.floor(Math.log10(value)));
      const normalized = value / magnitude;
      const nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
      return nice * magnitude;
    }

    function drawChart() {
      ctx.clearRect(0, 0, width, height);

      const all = pointsDownload.concat(pointsUpload);
      const rawMax = Math.max.apply(null, all) * 1.15;
      const yMax = Math.max(niceMax(rawMax), 200);
      const padding = { top: 14, right: 12, bottom: 24, left: 46 };
      const plotW = width - padding.left - padding.right;
      const plotH = height - padding.top - padding.bottom;

      if (plotW <= 10 || plotH <= 10) {
        return;
      }

      /* 横向网格 */
      ctx.lineWidth = 1;
      ctx.font = "11px 'Cascadia Code', Consolas, monospace";
      for (let i = 0; i <= 4; i += 1) {
        const y = padding.top + (plotH * i) / 4;
        const label = Math.round(yMax - (yMax * i) / 4);
        ctx.strokeStyle = "rgba(48, 54, 61, 0.55)";
        ctx.beginPath();
        ctx.moveTo(padding.left, y);
        ctx.lineTo(width - padding.right, y);
        ctx.stroke();
        ctx.fillStyle = "rgba(157, 167, 179, 0.75)";
        ctx.textAlign = "right";
        ctx.textBaseline = "middle";
        ctx.fillText(String(label), padding.left - 8, y);
      }

      function linePath(series, color, fill) {
        if (series.length < 2) {
          return;
        }
        const stepX = plotW / (MAX_POINTS - 1);
        const yFor = function (value) {
          return padding.top + plotH - (value / yMax) * plotH;
        };

        ctx.beginPath();
        series.forEach(function (value, index) {
          const x = padding.left + index * stepX;
          const y = yFor(value);
          if (index === 0) {
            ctx.moveTo(x, y);
          } else {
            ctx.lineTo(x, y);
          }
        });

        if (fill) {
          const gradient = ctx.createLinearGradient(0, padding.top, 0, height - padding.bottom);
          gradient.addColorStop(0, "rgba(88, 166, 255, 0.28)");
          gradient.addColorStop(1, "rgba(88, 166, 255, 0.02)");
          ctx.save();
          ctx.lineTo(padding.left + plotW, height - padding.bottom);
          ctx.lineTo(padding.left, height - padding.bottom);
          ctx.closePath();
          ctx.fillStyle = gradient;
          ctx.fill();
          ctx.restore();
        }

        ctx.beginPath();
        series.forEach(function (value, index) {
          const x = padding.left + index * stepX;
          const y = yFor(value);
          if (index === 0) {
            ctx.moveTo(x, y);
          } else {
            ctx.lineTo(x, y);
          }
        });
        ctx.strokeStyle = color;
        ctx.lineWidth = 2;
        ctx.lineJoin = "round";
        ctx.lineCap = "round";
        ctx.stroke();
      }

      linePath(pointsUpload, "#3fb950", false);
      linePath(pointsDownload, "#58a6ff", true);

      /* 下载均值参考线 */
      const avgDownload = pointsDownload.reduce(function (sum, value) {
        return sum + value;
      }, 0) / Math.max(pointsDownload.length, 1);
      const avgY = padding.top + plotH - (avgDownload / yMax) * plotH;
      ctx.save();
      ctx.setLineDash([5, 5]);
      ctx.strokeStyle = "rgba(188, 140, 255, 0.8)";
      ctx.lineWidth = 1.2;
      ctx.beginPath();
      ctx.moveTo(padding.left, avgY);
      ctx.lineTo(width - padding.right, avgY);
      ctx.stroke();
      ctx.restore();
      ctx.fillStyle = "rgba(188, 140, 255, 0.85)";
      ctx.textAlign = "left";
      ctx.textBaseline = "bottom";
      ctx.font = "10px 'Cascadia Code', Consolas, monospace";
      ctx.fillText(siteT("均值 ") + avgDownload.toFixed(0), padding.left + 6, avgY - 3);
    }

    function tick(now) {
      if (!lastPush) {
        lastPush = now;
          startTime = now;
      }

      const pushInterval = 200;
      while (now - lastPush >= pushInterval) {
        lastPush += pushInterval;

        downBase = clamp(downBase + randomBetween(-12, 14), 360, 560);
        upBase = clamp(upBase + randomBetween(-4, 5), 86, 156);

        const lastDown = pointsDownload[pointsDownload.length - 1];
        const lastUp = pointsUpload[pointsUpload.length - 1];
        pointsDownload.push(nextPoint(lastDown, downBase, 55, 230, 700));
        pointsUpload.push(nextPoint(lastUp, upBase, 18, 45, 230));

        if (pointsDownload.length > MAX_POINTS) {
          pointsDownload.shift();
          pointsUpload.shift();
        }
      }

      elapsedSeconds = Math.floor((now - startTime) / 1000);
        if (false) {
        // elapsedSeconds += 1;
      }

      const currentDown = pointsDownload[pointsDownload.length - 1] || 0;
      const currentUp = pointsUpload[pointsUpload.length - 1] || 0;
      if (demoDownload) {
        demoDownload.textContent = currentDown.toFixed(2);
      }
      if (demoUpload) {
        demoUpload.textContent = currentUp.toFixed(2);
      }
      if (demoTotal) {
        demoTotal.textContent = (currentDown + currentUp).toFixed(2);
      }
      if (demoElapsed) {
        const minutes = String(Math.floor(elapsedSeconds / 60)).padStart(2, "0");
        const seconds = String(elapsedSeconds % 60).padStart(2, "0");
        demoElapsed.textContent = minutes + ":" + seconds;
      }

      drawChart();
      requestAnimationFrame(tick);
    }

    resize();
    window.addEventListener("resize", resize);
    requestAnimationFrame(tick);
  }

  /* ---------- 页脚年份 ---------- */
  const yearEl = document.getElementById("year");
  if (yearEl) {
    yearEl.textContent = String(new Date().getFullYear());
  }
})();
