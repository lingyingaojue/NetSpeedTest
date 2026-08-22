/* NetSpeedTest Web 控制台 */
(function () {
  "use strict";

  var API_BASE = "";
  var statusTimer = null;
  var wasRunning = false;
  var currentMode = "download";
  var currentView = "speed";
  var webLang = localStorage.getItem("nst-web-lang") || "zh";
  var webTheme = localStorage.getItem("nst-web-theme") || "dark";
  var textNodes = [];
  var textNodesCaptured = false;
  var historyPage = 1;
  var historyPageSize = 10;
  var historyTotal = 0;
  var editingProfileId = null;
  var chartDl = [];
  var chartUl = [];
  var chartMaxPoints = 120;

  function $(id) { return document.getElementById(id); }

  function esc(value) {
    return String(value == null ? "" : value)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#39;");
  }

  var I18N_EN = {
    "Web 控制台": "Web Console",
    "测速": "Speed Test",
    "历史": "History",
    "配置": "Profiles",
    "设置": "Settings",
    "服务器": "Server",
    "测速控制台": "Speed Test Console",
    "实时速率 · 延迟 · 多网卡选择": "Live speed · Latency · Multi-NIC",
    "下载": "Download",
    "上传": "Upload",
    "总速率": "Total",
    "平均总速率": "Avg Total",
    "内网延迟": "LAN Latency",
    "外网延迟": "WAN Latency",
    "抖动": "Jitter",
    "丢包率": "Packet Loss",
    "流量": "Traffic",
    "线程": "Threads",
    "已传输": "Transferred",
    "活跃": "Active",
    "实时速率": "Live Rate",
    "测速模式": "Test Mode",
    "下载测速": "Download",
    "上传测速": "Upload",
    "双向测速": "Duplex",
    "▶ 开始测速": "▶ Start Test",
    "■ 停止": "■ Stop",
    "当前配置：": "Profile: ",
    "最近结果：": "Last result: ",
    "暂无": "None",
    "选择网卡": "Select NIC",
    "刷新": "Refresh",
    "测速历史": "Test History",
    "SQLite 持久化记录": "SQLite persisted records",
    "清空全部": "Clear All",
    "上一页": "Prev",
    "下一页": "Next",
    "测速配置": "Test Profiles",
    "管理下载 / 上传 URL 配置": "Manage download / upload URL profiles",
    "＋ 新建配置": "+ New Profile",
    "下载地址": "Download URLs",
    "上传地址": "Upload URLs",
    "无": "None",
    "编辑": "Edit",
    "删除": "Delete",
    "编辑配置": "Edit Profile",
    "新建配置": "New Profile",
    "名称": "Name",
    "下载 URL（每行一个）": "Download URLs (one per line)",
    "上传 URL（每行一个）": "Upload URLs (one per line)",
    "取消": "Cancel",
    "保存": "Save",
    "测速参数": "Test Parameters",
    "测速引擎": "Engine",
    "网络监控": "Network Monitor",
    "掉速补偿": "Compensation",
    "偏好设置": "Preferences",
    "保存后立即生效": "Applied immediately after saving",
    "保存设置": "Save Settings",
    "Web 服务器": "Web Server",
    "本机 HTTP 服务信息": "Local HTTP server information",
    "状态": "Status",
    "端口": "Port",
    "地址": "Address",
    "REST API": "REST API",
    "已连接": "Connected",
    "连接中断": "Disconnected",
    "连接中…": "Connecting…",
    "打开新窗口 ↗": "Open in new tab ↗",
    "v1.4.1 · 本机服务": "v1.4.1 · Local service",
    "请至少选择一张网卡": "Please select at least one NIC",
    "网卡选择已保存": "NIC selection saved",
    "无 IP": "No IP",
    "未找到网卡": "No NIC found",
    "测速已启动": "Test started",
    "已发送停止指令": "Stop command sent",
    "网卡列表已刷新": "NIC list refreshed",
    "加载中…": "Loading…",
    "暂无测速记录": "No test records",
    "确定删除这条记录吗？": "Delete this record?",
    "已删除": "Deleted",
    "删除失败：": "Delete failed: ",
    "确定清空全部测速记录吗？此操作不可恢复。": "Clear all test records? This cannot be undone.",
    "已清空全部记录": "All records cleared",
    "清空失败：": "Clear failed: ",
    "暂无配置，点击右上角新建": "No profiles yet. Click New Profile",
    "请输入配置名称": "Please enter a profile name",
    "配置已保存": "Profile saved",
    "保存失败：": "Save failed: ",
    "读取配置失败：": "Load profile failed: ",
    "确定删除该配置吗？": "Delete this profile?",
    "配置已删除": "Profile deleted",
    "设置已保存": "Settings saved",
    "启动失败：": "Start failed: ",
    "停止失败：": "Stop failed: ",
    "运行中": "Running",
    "已停止": "Stopped",
    "无法获取": "Unavailable",
    "并发线程数（1–512）": "Thread count (1–512)",
    "整体超时（秒，5–600）": "Timeout (sec, 5–600)",
    "平均计量延迟（秒，1–30）": "Average delay (sec, 1–30)",
    "速率平滑窗口（秒，0.5–10）": "Rate window (sec, 0.5–10)",
    "网卡轮询间隔（ms，200–5000）": "NIC poll interval (ms, 200–5000)",
    "线程启动间隔（ms，0–5000）": "Thread ramp-up (ms, 0–5000)",
    "延迟采样间隔（ms，500–10000）": "Latency interval (ms, 500–10000)",
    "抖动探测主机": "Jitter target host",
    "丢包率探测主机": "Packet loss target host",
    "抖动采样间隔（ms，500–5000）": "Jitter interval (ms, 500–5000)",
    "丢包率采样间隔（ms，500–5000）": "Packet loss interval (ms, 500–5000)",
    "掉速补偿阈值（0.3–0.8）": "Compensation threshold (0.3–0.8)",
    "补偿额外线程（0–64）": "Extra threads (0–64)",
    "补偿确认时间（秒，1–10）": "Confirm time (sec, 1–10)",
    "启用掉速紧急补偿": "Enable drop compensation",
    "CPU 自适应线程上限": "CPU adaptive thread limit",
    "启用 Web 服务器（关闭后本页面将断开）": "Enable Web server (page disconnects if off)",
    "主题": "Theme",
    "暗色": "Dark",
    "亮色": "Light",
    "语言": "Language",
    "简体中文": "Simplified Chinese",
    "English": "English"
  };

  function t(text) {
    if (webLang === "en" && I18N_EN[text]) return I18N_EN[text];
    return text;
  }

  function captureTextNodes() {
    if (textNodesCaptured) return;
    textNodesCaptured = true;
    var walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, null);
    var node;
    while ((node = walker.nextNode())) {
      if (node.nodeValue && node.nodeValue.trim() && node.parentElement && node.parentElement.tagName !== "SCRIPT") {
        textNodes.push({ node: node, original: node.nodeValue });
      }
    }
  }

  function applyLanguage(lang) {
    webLang = lang;
    localStorage.setItem("nst-web-lang", lang);
    captureTextNodes();

    textNodes.forEach(function (item) {
      var value = item.original;
      if (lang === "en") {
        Object.keys(I18N_EN).sort(function (a, b) { return b.length - a.length; }).forEach(function (key) {
          value = value.split(key).join(I18N_EN[key]);
        });
      }
      item.node.nodeValue = value;
    });

    document.documentElement.lang = lang === "en" ? "en" : "zh-CN";
    var select = $("langSwitch");
    if (select) select.value = lang;
    setConnected(window._connected !== false);
    refreshStaticDynamicText();
  }

  function refreshStaticDynamicText() {
    if (currentView === "history") loadHistory(historyPage);
    if (currentView === "profiles") loadProfiles();
    if (currentView === "settings") loadSettings();
    if (currentView === "server") loadServerInfo();
    refreshStatus();
  }

  function serverStatusText(text) {
    if (webLang !== "en") return text || "就绪";
    var value = text || "Ready";
    var map = {
      "就绪": "Ready",
      "已取消": "Cancelled",
      "测速完成": "Complete",
      "测速中": "Testing",
      "多网卡测速完成": "Multi-NIC complete",
      "失败": "Failed",
      "启动测速失败": "Start failed",
      "无法连接服务器": "Cannot connect to server"
    };
    Object.keys(map).forEach(function (key) {
      value = value.split(key).join(map[key]);
    });
    return value;
  }

  function applyTheme(theme) {
    webTheme = theme;
    localStorage.setItem("nst-web-theme", theme);
    document.documentElement.classList.toggle("light", theme === "light");
    var btn = $("themeToggle");
    if (btn) btn.textContent = theme === "light" ? "☀️" : "🌙";
  }

  function toast(message, type) {
    var el = $("toast");
    el.textContent = message;
    el.hidden = false;
    el.className = "toast" + (type ? " " + type : "");
    clearTimeout(el._timer);
    el._timer = setTimeout(function () { el.hidden = true; }, 2600);
  }

  async function api(path, options) {
    var opts = options || {};
    var headers = Object.assign({}, opts.headers || {});
    if (opts.body) headers["Content-Type"] = "application/json";
    var res = await fetch(API_BASE + path, Object.assign({}, opts, { headers: headers }));
    var text = await res.text();
    var data = null;
    if (text) {
      try { data = JSON.parse(text); } catch (e) { data = null; }
    }
    if (!res.ok) {
      throw new Error((data && data.error) || ("HTTP " + res.status));
    }
    return data;
  }

  /* ---------------- 格式化 ---------------- */

  function fmtRate(v) {
    if (v == null || isNaN(v)) return "--";
    v = Number(v);
    if (v >= 1000) return (v / 1000).toFixed(2) + " Gbps";
    if (v >= 1) return v.toFixed(2) + " Mbps";
    return (v * 1000).toFixed(0) + " Kbps";
  }

  function fmtNumber(v) {
    if (v == null || isNaN(v)) return "--";
    return Number(v).toFixed(1);
  }

  function fmtPacketLoss(v) {
    if (v == null || isNaN(v)) return "--";
    return Number(v).toFixed(1) + "%";
  }

  function fmtBytes(v) {
    if (v == null || isNaN(v)) return "--";
    v = Number(v);
    if (v >= 1073741824) return (v / 1073741824).toFixed(2) + " GB";
    if (v >= 1048576) return (v / 1048576).toFixed(2) + " MB";
    if (v >= 1024) return (v / 1024).toFixed(1) + " KB";
    return v + " B";
  }

  function fmtDateTime(value) {
    if (!value) return "--";
    var d = new Date(value);
    if (isNaN(d.getTime())) return esc(value);
    function p(n) { return String(n).padStart(2, "0"); }
    return d.getFullYear() + "-" + p(d.getMonth() + 1) + "-" + p(d.getDate()) +
      " " + p(d.getHours()) + ":" + p(d.getMinutes()) + ":" + p(d.getSeconds());
  }

  function fmtElapsed(seconds) {
    if (seconds == null) return "00:00";
    var s = Math.floor(Number(seconds));
    return String(Math.floor(s / 60)).padStart(2, "0") + ":" + String(s % 60).padStart(2, "0");
  }

  function testTypeText(type) {
    if (type === "上传") return "上传";
    if (type === "双向") return "双向";
    return "下载";
  }

  /* ---------------- 视图切换 ---------------- */

  function switchView(name) {
    currentView = name;
    document.querySelectorAll(".nav-btn").forEach(function (btn) {
      btn.classList.toggle("active", btn.dataset.view === name);
    });
    document.querySelectorAll(".view").forEach(function (view) {
      view.classList.toggle("active", view.id === "view-" + name);
    });

    if (name === "history") loadHistory(historyPage);
    if (name === "profiles") loadProfiles();
    if (name === "settings") loadSettings();
    if (name === "server") loadServerInfo();
  }

  document.querySelectorAll(".nav-btn").forEach(function (btn) {
    btn.addEventListener("click", function () { switchView(btn.dataset.view); });
  });

  $("themeToggle").addEventListener("click", async function () {
    var next = webTheme === "light" ? "dark" : "light";
    applyTheme(next);
    try {
      await api("/api/settings", {
        method: "POST",
        body: JSON.stringify({ theme: next === "light" ? "Light" : "Dark" })
      });
    } catch (err) {
      toast("主题同步失败：" + err.message, "err");
    }
  });

  $("langSwitch").addEventListener("change", async function () {
    var next = $("langSwitch").value === "en" ? "en" : "zh";
    applyLanguage(next);
    try {
      await api("/api/settings", {
        method: "POST",
        body: JSON.stringify({ language: next === "en" ? "EnUS" : "ZhCN" })
      });
    } catch (err) {
      toast("语言同步失败：" + err.message, "err");
    }
  });

  /* ---------------- 连接与状态轮询 ---------------- */

  function setConnected(ok) {
    window._connected = ok;
    var dot = $("connDot");
    var text = $("connText");
    if (ok) {
      dot.className = "conn-dot ok";
      text.textContent = t("已连接");
    } else {
      dot.className = "conn-dot err";
      text.textContent = t("连接中断");
    }
  }

  function applyStatus(s) {
    setConnected(true);

    var running = !!s.running;
    $("statusText").textContent = serverStatusText(s.status || (running ? "测速中" : "就绪"));
    $("statusPill").classList.toggle("running", running);

    $("mDownload").textContent = fmtRate(s.downloadMbps);
    $("mUpload").textContent = fmtRate(s.uploadMbps);
    $("mTotal").textContent = fmtRate(s.totalMbps);
    $("mAvgTotal").textContent = fmtRate(s.averageTotalMbps);
    $("mLatency").textContent = fmtNumber(s.latencyMs);
    $("mWan").textContent = fmtNumber(s.wanLatencyMs);
    $("mJitter").textContent = fmtNumber(s.jitterMs);
    $("mPacketLoss").textContent = fmtPacketLoss(s.packetLossPercent);
    $("mPacketLossDetail").textContent = (s.packetLossSent == null || s.packetLossReceived == null)
      ? "--"
      : (webLang === "en" ? "recv " : "收 ") + s.packetLossReceived + " / " + s.packetLossSent;
    $("mTraffic").textContent = fmtBytes(s.totalBytes);
    $("mThreads").textContent = (s.activeThreads == null ? "--" : s.activeThreads);
    $("chartElapsed").textContent = fmtElapsed(s.elapsedSeconds);
    $("currentProfile").textContent = s.currentProfile || "默认配置";
    $("startBtn").disabled = running;
    $("stopBtn").disabled = !running;

    if (s.recentResult) {
      $("recentResult").textContent =
        t("下载") + " " + fmtRate(s.recentResult.downloadMbps) +
        " · " + t("上传") + " " + fmtRate(s.recentResult.uploadMbps) +
        " · " + (webLang === "en" ? "Latency" : "延迟") + " " + fmtNumber(s.recentResult.latencyMs) + " ms";
    }

    if (running && !wasRunning) {
      chartDl = [];
      chartUl = [];
    }
    wasRunning = running;

    if (running) {
      if (s.downloadMbps != null) pushChart(chartDl, Number(s.downloadMbps));
      if (s.uploadMbps != null) pushChart(chartUl, Number(s.uploadMbps));
    }

    drawChart();
  }

  function pushChart(arr, value) {
    arr.push(value);
    if (arr.length > chartMaxPoints) arr.shift();
  }

  async function refreshStatus() {
    try {
      var status = await api("/api/status");
      applyStatus(status);
    } catch (err) {
      setConnected(false);
      $("statusText").textContent = serverStatusText("无法连接服务器");
      $("startBtn").disabled = true;
      $("stopBtn").disabled = true;
    }
  }

  /* ---------------- 实时图表 ---------------- */

  function drawChart() {
    var canvas = $("rateChart");
    if (!canvas || !canvas.getContext) return;
    var ctx = canvas.getContext("2d");
    var rect = canvas.getBoundingClientRect();
    var dpr = window.devicePixelRatio || 1;
    var width = Math.max(rect.width, 10);
    var height = Math.max(rect.height, 10);
    if (canvas.width !== Math.round(width * dpr) || canvas.height !== Math.round(height * dpr)) {
      canvas.width = Math.round(width * dpr);
      canvas.height = Math.round(height * dpr);
    }
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, width, height);

    var pad = { top: 14, right: 14, bottom: 24, left: 52 };
    var plotW = width - pad.left - pad.right;
    var plotH = height - pad.top - pad.bottom;
    if (plotW < 20 || plotH < 20) return;

    var all = chartDl.concat(chartUl);
    var max = 10;
    all.forEach(function (v) { if (v > max) max = v; });
    var yMax = niceMax(max * 1.15);

    ctx.lineWidth = 1;
    ctx.font = "11px Consolas, monospace";
    for (var i = 0; i <= 4; i++) {
      var y = pad.top + (plotH * i) / 4;
      var label = Math.round(yMax - (yMax * i) / 4);
      ctx.strokeStyle = "rgba(48,54,61,0.55)";
      ctx.beginPath();
      ctx.moveTo(pad.left, y);
      ctx.lineTo(width - pad.right, y);
      ctx.stroke();
      ctx.fillStyle = "rgba(139,148,158,0.7)";
      ctx.textAlign = "right";
      ctx.textBaseline = "middle";
      ctx.fillText(String(label), pad.left - 8, y);
    }

    function drawLine(arr, color, fill) {
      if (arr.length < 2) return;
      var stepX = plotW / (chartMaxPoints - 1);
      var yFor = function (v) { return pad.top + plotH - (v / yMax) * plotH; };

      if (fill) {
        ctx.beginPath();
        arr.forEach(function (v, idx) {
          var x = pad.left + (idx + chartMaxPoints - arr.length) * stepX;
          var yv = yFor(v);
          if (idx === 0) ctx.moveTo(x, yv); else ctx.lineTo(x, yv);
        });
        var grad = ctx.createLinearGradient(0, pad.top, 0, height - pad.bottom);
        grad.addColorStop(0, "rgba(88,166,255,0.24)");
        grad.addColorStop(1, "rgba(88,166,255,0.02)");
        ctx.lineTo(pad.left + plotW, height - pad.bottom);
        ctx.lineTo(pad.left + (chartMaxPoints - arr.length) * stepX, height - pad.bottom);
        ctx.closePath();
        ctx.fillStyle = grad;
        ctx.fill();
      }

      ctx.beginPath();
      arr.forEach(function (v, idx) {
        var x = pad.left + (idx + chartMaxPoints - arr.length) * stepX;
        var yv = yFor(v);
        if (idx === 0) ctx.moveTo(x, yv); else ctx.lineTo(x, yv);
      });
      ctx.strokeStyle = color;
      ctx.lineWidth = 2;
      ctx.lineJoin = "round";
      ctx.lineCap = "round";
      ctx.stroke();
    }

    drawLine(chartUl, "#3fb950", false);
    drawLine(chartDl, "#58a6ff", true);
  }

  function niceMax(value) {
    var magnitude = Math.pow(10, Math.floor(Math.log10(value)));
    var normalized = value / magnitude;
    var nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
    return nice * magnitude;
  }

  window.addEventListener("resize", drawChart);

  /* ---------------- 网卡 ---------------- */

  async function loadAdapters() {
    var list = $("adapterList");
    try {
      var adapters = await api("/api/adapters");
      if (!adapters.length) {
        list.innerHTML = '<div class="empty">' + esc(t("未找到网卡")) + "</div>";
        return;
      }
      list.innerHTML = adapters.map(function (a) {
        return '<label class="adapter-item' + (a.selected ? " checked" : "") + '">' +
          '<input type="checkbox" data-id="' + esc(a.id) + '" ' + (a.selected ? "checked" : "") + ' />' +
          '<span><strong>' + esc(a.name) + "</strong>" +
          "<small>" + esc(a.ip || t("无 IP")) + " · " + esc(t(a.status || a.type || "")) + "</small></span>" +
          "</label>";
      }).join("");

      list.querySelectorAll("input[type=checkbox]").forEach(function (input) {
        input.addEventListener("change", async function () {
          var item = input.closest(".adapter-item");
          item.classList.toggle("checked", input.checked);

          var ids = selectedAdapterIds();
          if (!ids.length) {
            input.checked = true;
            item.classList.add("checked");
            toast(t("请至少选择一张网卡"), "err");
            return;
          }

          try {
            await api("/api/adapters/select", {
              method: "POST",
              body: JSON.stringify({ adapterIds: ids })
            });
            toast(t("网卡选择已保存"), "ok");
          } catch (err) {
            input.checked = !input.checked;
            item.classList.toggle("checked", input.checked);
            loadAdapters();
            toast(t("保存失败：") + err.message, "err");
          }
        });
      });
    } catch (err) {
      list.innerHTML = '<div class="empty">' + esc(err.message) + "</div>";
    }
  }

  function selectedAdapterIds() {
    var ids = [];
    document.querySelectorAll("#adapterList input:checked").forEach(function (input) {
      ids.push(input.dataset.id);
    });
    return ids;
  }

  /* ---------------- 测速控制 ---------------- */

  document.querySelectorAll(".mode-btn").forEach(function (btn) {
    btn.addEventListener("click", function () {
      currentMode = btn.dataset.mode;
      document.querySelectorAll(".mode-btn").forEach(function (b) {
        b.classList.toggle("active", b === btn);
      });
    });
  });

  $("startBtn").addEventListener("click", async function () {
    var ids = selectedAdapterIds();
    if (!ids.length) {
      toast("请至少选择一张网卡", "err");
      return;
    }
    try {
      await api("/api/test/start", {
        method: "POST",
        body: JSON.stringify({ mode: currentMode, adapterIds: ids })
      });
      toast(t("测速已启动"), "ok");
      setTimeout(refreshStatus, 500);
    } catch (err) {
      toast(t("启动失败：") + err.message, "err");
    }
  });

  $("stopBtn").addEventListener("click", async function () {
    try {
      await api("/api/test/stop", { method: "POST" });
      toast(t("已发送停止指令"), "ok");
      setTimeout(refreshStatus, 500);
    } catch (err) {
      toast(t("停止失败：") + err.message, "err");
    }
  });

  $("adapterRefreshBtn").addEventListener("click", function () {
    loadAdapters().then(function () { toast(t("网卡列表已刷新"), "ok"); });
  });

  /* ---------------- 历史 ---------------- */

  async function loadHistory(page) {
    historyPage = page || 1;
    var body = $("historyBody");
    body.innerHTML = '<tr><td colspan="11" class="empty">' + esc(t("加载中…")) + "</td></tr>";
    try {
      var data = await api("/api/history?page=" + historyPage + "&pageSize=" + historyPageSize);
      historyTotal = data.total || 0;
      var records = data.records || [];
      var pages = Math.max(1, Math.ceil(historyTotal / historyPageSize));

      if (!records.length) {
        body.innerHTML = '<tr><td colspan="11" class="empty">' + esc(t("暂无测速记录")) + "</td></tr>";
      } else {
        body.innerHTML = records.map(function (r) {
          return "<tr>" +
            "<td>" + fmtDateTime(r.timestamp) + "</td>" +
            "<td>" + esc(t(testTypeText(r.testType))) + "</td>" +
            "<td>" + esc(fmtRate(r.downloadMbps)) + "</td>" +
            "<td>" + esc(fmtRate(r.uploadMbps)) + "</td>" +
            "<td>" + esc(fmtRate(r.averageTotalMbps)) + "</td>" +
            "<td>" + esc(fmtNumber(r.latencyMs)) + "</td>" +
            "<td>" + esc(fmtNumber(r.jitterMs)) + "</td>" +
            "<td>" + esc(fmtPacketLoss(r.packetLoss)) + "</td>" +
            "<td>" + esc(r.networkAdapterName || "--") + "</td>" +
            "<td>" + esc(r.nodeName || "--") + "</td>" +
            '<td><button class="delete-btn" data-id="' + esc(r.id) + '" title="删除这条">🗑</button></td>' +
            "</tr>";
        }).join("");
      }

      $("historyPageInfo").textContent = historyPage + " / " + pages;
      $("historyPrev").disabled = historyPage <= 1;
      $("historyNext").disabled = historyPage >= pages;
    } catch (err) {
      body.innerHTML = '<tr><td colspan="11" class="empty">' + esc(err.message) + "</td></tr>";
    }
  }

  $("historyRefreshBtn").addEventListener("click", function () { loadHistory(historyPage); });
  $("historyPrev").addEventListener("click", function () { if (historyPage > 1) loadHistory(historyPage - 1); });
  $("historyNext").addEventListener("click", function () { loadHistory(historyPage + 1); });

  $("historyBody").addEventListener("click", async function (event) {
    var btn = event.target.closest(".delete-btn");
    if (!btn) return;
    if (!confirm(t("确定删除这条记录吗？"))) return;
    try {
      await api("/api/history?id=" + encodeURIComponent(btn.dataset.id), { method: "DELETE" });
      toast(t("已删除"), "ok");
      loadHistory(historyPage);
    } catch (err) {
      toast(t("删除失败：") + err.message, "err");
    }
  });

  $("historyClearBtn").addEventListener("click", async function () {
    if (!confirm(t("确定清空全部测速记录吗？此操作不可恢复。"))) return;
    try {
      await api("/api/history", { method: "DELETE" });
      toast(t("已清空全部记录"), "ok");
      loadHistory(1);
    } catch (err) {
      toast(t("清空失败：") + err.message, "err");
    }
  });

  /* ---------------- 配置 ---------------- */

  async function loadProfiles() {
    var list = $("profileList");
    list.innerHTML = '<div class="empty">' + esc(t("加载中…")) + "</div>";
    try {
      var profiles = await api("/api/profiles");
      if (!profiles.length) {
        list.innerHTML = '<div class="empty">' + esc(t("暂无配置，点击右上角新建")) + "</div>";
        return;
      }
      list.innerHTML = profiles.map(function (p) {
        var dl = (p.downloadUrls || []).map(function (u) { return "<li>" + esc(u) + "</li>"; }).join("");
        var ul = (p.uploadUrls || []).map(function (u) { return "<li>" + esc(u) + "</li>"; }).join("");
        return '<article class="profile-card">' +
          "<h3><span>" + esc(p.name) + "</span><span class=\"muted\">" + esc(p.id || "") + "</span></h3>" +
          '<p class="muted">' + esc(t("下载地址")) + '</p><ul class="profile-urls">' + (dl || '<li class="empty-item">' + esc(t("无")) + "</li>") + "</ul>" +
          '<p class="muted">' + esc(t("上传地址")) + '</p><ul class="profile-urls">' + (ul || '<li class="empty-item">' + esc(t("无")) + "</li>") + "</ul>" +
          '<div class="profile-actions">' +
          '<button class="btn ghost small profile-edit" data-id="' + esc(p.id) + '">' + esc(t("编辑")) + "</button>" +
          '<button class="btn danger small profile-delete" data-id="' + esc(p.id) + '">' + esc(t("删除")) + "</button>" +
          "</div></article>";
      }).join("");
    } catch (err) {
      list.innerHTML = '<div class="empty">' + esc(err.message) + "</div>";
    }
  }

  function openProfileModal(profile) {
    editingProfileId = profile ? profile.id : null;
    $("profileModalTitle").textContent = profile ? t("编辑配置") : t("新建配置");
    $("profileName").value = profile ? profile.name : "";
    $("profileDownloadUrls").value = profile ? (profile.downloadUrls || []).join("\n") : "";
    $("profileUploadUrls").value = profile ? (profile.uploadUrls || []).join("\n") : "";
    $("profileModal").hidden = false;
  }

  function closeProfileModal() {
    $("profileModal").hidden = true;
    editingProfileId = null;
  }

  function textToLines(value) {
    return value.split("\n").map(function (s) { return s.trim(); }).filter(Boolean);
  }

  $("profileNewBtn").addEventListener("click", function () { openProfileModal(null); });
  $("profileCancelBtn").addEventListener("click", closeProfileModal);
  $("profileModalClose").addEventListener("click", closeProfileModal);
  $("profileModal").addEventListener("click", function (e) {
    if (e.target === $("profileModal")) closeProfileModal();
  });

  $("profileSaveBtn").addEventListener("click", async function () {
    var name = $("profileName").value.trim();
    if (!name) { toast(t("请输入配置名称"), "err"); return; }
    var payload = {
      id: editingProfileId || undefined,
      name: name,
      downloadUrls: textToLines($("profileDownloadUrls").value),
      uploadUrls: textToLines($("profileUploadUrls").value)
    };
    try {
      await api("/api/profiles", { method: "POST", body: JSON.stringify(payload) });
      toast(t("配置已保存"), "ok");
      closeProfileModal();
      loadProfiles();
    } catch (err) {
      toast(t("保存失败：") + err.message, "err");
    }
  });

  $("profileList").addEventListener("click", async function (event) {
    var editBtn = event.target.closest(".profile-edit");
    var deleteBtn = event.target.closest(".profile-delete");
    if (!editBtn && !deleteBtn) return;

    if (editBtn) {
      try {
        var profiles = await api("/api/profiles");
        var profile = profiles.find(function (p) { return String(p.id) === String(editBtn.dataset.id); });
        if (profile) openProfileModal(profile);
      } catch (err) {
        toast(t("读取配置失败：") + err.message, "err");
      }
      return;
    }

    if (!confirm(t("确定删除该配置吗？"))) return;
    try {
      await api("/api/profiles", {
        method: "POST",
        body: JSON.stringify({ id: deleteBtn.dataset.id, delete: true })
      });
      toast(t("配置已删除"), "ok");
      loadProfiles();
    } catch (err) {
      toast(t("删除失败：") + err.message, "err");
    }
  });

  /* ---------------- 设置 ---------------- */

  function settingsSection(title, fields) {
    return '<section class="panel settings-card glass-card"><div class="settings-card-head"><h3>' + esc(t(title)) + '</h3></div><div class="settings-grid">' + fields + '</div></section>';
  }

  function numberField(key, label, min, max, step) {
    return '<label class="field"><span>' + label + "</span>" +
      '<input type="number" data-key="' + key + '" min="' + min + '" max="' + max + '" step="' + step + '" /></label>';
  }

  function boolField(key, label) {
    return '<label class="field check-field"><input type="checkbox" data-key="' + key + '" /><span>' + label + "</span></label>";
  }

  async function loadSettings() {
    var form = $("settingsForm");
    form.innerHTML = '<div class="empty">' + esc(t("加载中…")) + "</div>";
    try {
      var s = await api("/api/settings");
      var engineFields =
        numberField("threadCount", t("并发线程数（1–512）"), 1, 512, 1) +
        numberField("testTimeoutSec", t("整体超时（秒，5–600）"), 5, 600, 1) +
        numberField("averageDelaySec", t("平均计量延迟（秒，1–30）"), 1, 30, 1) +
        numberField("rateWindowSec", t("速率平滑窗口（秒，0.5–10）"), 0.5, 10, 0.1) +
        numberField("threadRampUpMs", t("线程启动间隔（ms，0–5000）"), 0, 5000, 10);

      var networkFields =
        numberField("nicPollIntervalMs", t("网卡轮询间隔（ms，200–5000）"), 200, 5000, 10) +
        numberField("latencyPollIntervalMs", t("延迟采样间隔（ms，500–10000）"), 500, 10000, 100) +
        '<label class="field"><span>' + esc(t("丢包率探测主机")) + '</span><input type="text" data-key="packetLossTargetHost" /></label>' +
        numberField("packetLossPollIntervalMs", t("丢包率采样间隔（ms，500–5000）"), 500, 5000, 100) +
        '<label class="field"><span>' + esc(t("抖动探测主机")) + '</span><input type="text" data-key="jitterTargetHost" /></label>' +
        numberField("jitterPollIntervalMs", t("抖动采样间隔（ms，500–5000）"), 500, 5000, 100);

      var compensationFields =
        numberField("compensationThreshold", t("掉速补偿阈值（0.3–0.8）"), 0.3, 0.8, 0.05) +
        numberField("compensationExtraThreads", t("补偿额外线程（0–64）"), 0, 64, 1) +
        numberField("compensationConfirmSec", t("补偿确认时间（秒，1–10）"), 1, 10, 1) +
        boolField("compensationEnabled", t("启用掉速紧急补偿")) +
        boolField("adaptiveThreadsEnabled", t("CPU 自适应线程上限"));

      var preferenceFields =
        boolField("webServerEnabled", t("启用 Web 服务器（关闭后本页面将断开）")) +
        '<label class="field"><span>' + esc(t("主题")) + '</span><select data-key="theme">' +
        '<option value="Dark">' + esc(t("暗色")) + '</option><option value="Light">' + esc(t("亮色")) + '</option></select></label>' +
        '<label class="field"><span>' + esc(t("语言")) + '</span><select data-key="language">' +
        '<option value="ZhCN">' + esc(t("简体中文")) + '</option><option value="EnUS">English</option></select></label>';

      form.innerHTML =
        settingsSection("测速引擎", engineFields) +
        settingsSection("网络监控", networkFields) +
        settingsSection("掉速补偿", compensationFields) +
        settingsSection("偏好设置", preferenceFields);

      form.querySelectorAll("[data-key]").forEach(function (input) {
        var key = input.dataset.key;
        if (key in s && s[key] != null) {
          if (input.type === "checkbox") input.checked = !!s[key];
          else input.value = s[key];
        }
      });

      if (s.theme) form.querySelector('select[data-key="theme"]').value = s.theme;
      if (s.language) form.querySelector('select[data-key="language"]').value = s.language;
    } catch (err) {
      form.innerHTML = '<div class="empty">' + esc(err.message) + "</div>";
    }
  }

  $("settingsSaveBtn").addEventListener("click", async function () {
    var payload = {};
    document.querySelectorAll("#settingsForm [data-key]").forEach(function (input) {
      var key = input.dataset.key;
      if (input.type === "checkbox") payload[key] = input.checked;
      else if (input.type === "number") payload[key] = Number(input.value);
      else payload[key] = input.value;
    });

    try {
      await api("/api/settings", { method: "POST", body: JSON.stringify(payload) });
      toast(t("设置已保存"), "ok");
      refreshStatus();
    } catch (err) {
      toast(t("保存失败：") + err.message, "err");
    }
  });

  /* ---------------- 服务器 ---------------- */

  async function loadServerInfo() {
    try {
      var info = await api("/api/server");
      $("serverStatus").textContent = info.enabled ? t("运行中") : t("已停止");
      $("serverPort").textContent = info.port;
      $("serverUrl").textContent = info.url;
      $("serverUrl").href = info.url;
    } catch (err) {
      $("serverStatus").textContent = t("无法获取");
    }
  }

  async function loadPreferences() {
    try {
      var s = await api("/api/settings");
      if (!localStorage.getItem("nst-web-theme")) {
        applyTheme(s.theme === "Light" ? "light" : "dark");
      }
      if (!localStorage.getItem("nst-web-lang")) {
        applyLanguage(s.language === "EnUS" ? "en" : "zh");
      }
    } catch (err) {
      /* 服务器未就绪时保持本地偏好 */
    }
  }

  /* ---------------- 玻璃拟态 + 交互光效 ---------------- */
  var cursorGlow = $("cursorGlow");
  var reducedMotion = window.matchMedia ? window.matchMedia("(prefers-reduced-motion: reduce)").matches : false;
  var finePointer = window.matchMedia ? window.matchMedia("(hover: hover) and (pointer: fine)").matches : true;

  if (cursorGlow && !reducedMotion && finePointer) {
    var glowX = window.innerWidth / 2;
    var glowY = window.innerHeight / 2;
    var targetX = glowX;
    var targetY = glowY;
    var glowRaf = 0;

    function renderGlow() {
      var dx = targetX - glowX;
      var dy = targetY - glowY;
      glowX += dx * 0.13;
      glowY += dy * 0.13;
      cursorGlow.style.transform = "translate3d(" + glowX + "px, " + glowY + "px, 0) translate(-50%, -50%)";
      if (Math.abs(dx) > 0.15 || Math.abs(dy) > 0.15) {
        glowRaf = requestAnimationFrame(renderGlow);
      } else {
        glowRaf = 0;
      }
    }

    function wakeGlow() {
      if (!glowRaf) glowRaf = requestAnimationFrame(renderGlow);
    }

    window.addEventListener("mousemove", function (event) {
      targetX = event.clientX;
      targetY = event.clientY;
      wakeGlow();
    }, { passive: true });

    document.documentElement.addEventListener("mouseleave", function () {
      targetX = -300;
      targetY = -300;
      wakeGlow();
    });

    wakeGlow();
  }

  /* 动态生成的卡片也获得鼠标聚光交互 */
  document.addEventListener("mousemove", function (event) {
    var card = event.target.closest ? event.target.closest(".glass-card") : null;
    if (!card) return;
    var rect = card.getBoundingClientRect();
    card.style.setProperty("--mx", (event.clientX - rect.left) + "px");
    card.style.setProperty("--my", (event.clientY - rect.top) + "px");
  }, { passive: true });

  var glassCards = document.querySelectorAll(".panel, .metric, .profile-card, .adapter-item");
  if (!reducedMotion && window.matchMedia && window.matchMedia("(hover: hover)").matches) {
    glassCards.forEach(function (card) {
      card.classList.add("glass-card");
      card.addEventListener("mousemove", function (event) {
        var rect = card.getBoundingClientRect();
        card.style.setProperty("--mx", (event.clientX - rect.left) + "px");
        card.style.setProperty("--my", (event.clientY - rect.top) + "px");
      });
      /* 鼠标离开时保留最后位置，避免光斑跳回中心闪一下 */
    });
  } else {
    glassCards.forEach(function (card) { card.classList.add("glass-card"); });
  }

  /* ---------------- 启动 ---------------- */

  applyTheme(webTheme);
  applyLanguage(webLang);
  loadAdapters();
  refreshStatus();
  loadServerInfo();
  loadPreferences();
  statusTimer = setInterval(refreshStatus, 1000);
  window.addEventListener("beforeunload", function () {
    if (statusTimer) clearInterval(statusTimer);
  });
})();
