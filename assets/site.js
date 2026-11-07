/* ============================================================
   NetSpeedTest — 官网交互脚本（无第三方依赖，渐进增强）
   主题切换 / 中英切换 / 移动导航 / 滚动高亮 / 计数器 /
   模拟波形 / 复制邮箱 / 页脚年份
   ============================================================ */

(function () {
  "use strict";

  var root = document.documentElement;
  var reduceMotion = window.matchMedia
    ? window.matchMedia("(prefers-reduced-motion: reduce)").matches
    : false;

  // ?motion=off 供截图/自动化测试使用：关闭计数器、滚动显现与波形动画。
  try {
    var query = new URLSearchParams(window.location.search);
    if (query.get("motion") === "off") {
      reduceMotion = true;
    }
  } catch (error) { /* 旧浏览器忽略 */ }

  /* ---------- 1. 翻译字典 ---------- */
  var dict = {};
  var dictNode = document.getElementById("i18n-data");
  if (dictNode) {
    try {
      var parsed = JSON.parse(dictNode.textContent);
      dict = (parsed && parsed.en) || {};
    } catch (error) {
      dict = {};
    }
  }

  function translate(key) {
    return Object.prototype.hasOwnProperty.call(dict, key) ? dict[key] : null;
  }

  function currentLang() {
    return root.getAttribute("data-lang") === "en" ? "en" : "zh";
  }

  var original = typeof WeakMap === "function" ? new WeakMap() : null;
  var langButtons = document.querySelectorAll("[data-lang]");
  var originalTitle = document.title;
  var descriptionMeta = document.querySelector('meta[name="description"]');
  var originalDescription = descriptionMeta ? descriptionMeta.getAttribute("content") : "";

  function applyLang(lang) {
    var english = lang === "en";
    var nodes = document.querySelectorAll("[data-i18n],[data-i18n-html],[data-i18n-aria],[data-i18n-title]");

    Array.prototype.forEach.call(nodes, function (node) {
      var stored = original && original.get(node);
      if (!stored) {
        stored = {
          html: node.innerHTML,
          aria: node.getAttribute("aria-label"),
          title: node.getAttribute("title")
        };
        if (original) {
          original.set(node, stored);
        }
      }

      if (node.hasAttribute("data-i18n")) {
        var textKey = node.getAttribute("data-i18n");
        var textValue = english ? translate(textKey) : null;
        node.textContent = textValue || stored.html;
      }

      if (node.hasAttribute("data-i18n-html")) {
        var htmlKey = node.getAttribute("data-i18n-html");
        var htmlValue = english ? translate(htmlKey) : null;
        node.innerHTML = htmlValue || stored.html;
      }

      if (node.hasAttribute("data-i18n-aria")) {
        var ariaKey = node.getAttribute("data-i18n-aria");
        var ariaValue = english ? translate(ariaKey) : null;
        if (ariaValue) {
          node.setAttribute("aria-label", ariaValue);
        } else if (stored.aria !== null) {
          node.setAttribute("aria-label", stored.aria);
        } else {
          node.removeAttribute("aria-label");
        }
      }

      if (node.hasAttribute("data-i18n-title")) {
        var titleKey = node.getAttribute("data-i18n-title");
        var titleValue = english ? translate(titleKey) : null;
        if (titleValue) {
          node.setAttribute("title", titleValue);
        } else if (stored.title !== null) {
          node.setAttribute("title", stored.title);
        } else {
          node.removeAttribute("title");
        }
      }
    });

    document.title = english ? (translate("meta.title") || originalTitle) : originalTitle;
    if (descriptionMeta) {
      var descValue = english ? translate("meta.description") : null;
      descriptionMeta.setAttribute("content", descValue || originalDescription);
    }

    root.setAttribute("lang", english ? "en" : "zh-CN");
    root.setAttribute("data-lang", english ? "en" : "zh");

    Array.prototype.forEach.call(langButtons, function (button) {
      var active = button.getAttribute("data-lang") === (english ? "en" : "zh");
      button.classList.toggle("is-active", active);
      button.setAttribute("aria-pressed", active ? "true" : "false");
    });
  }

  Array.prototype.forEach.call(langButtons, function (button) {
    button.addEventListener("click", function () {
      var lang = button.getAttribute("data-lang") === "en" ? "en" : "zh";
      if (lang === currentLang()) {
        return;
      }
      applyLang(lang);
      try {
        localStorage.setItem("nst-site-lang", lang);
      } catch (error) { /* 忽略隐私模式失败 */ }
    });
  });

  applyLang(currentLang());

  /* ---------- 2. 主题切换 ---------- */
  var themeButton = document.getElementById("themeToggle");
  var themeMeta = document.querySelector('meta[name="theme-color"]');
  var darkQuery = window.matchMedia ? window.matchMedia("(prefers-color-scheme: light)") : null;

  function applyTheme(theme) {
    var light = theme === "light";
    root.setAttribute("data-theme", light ? "light" : "dark");
    if (themeMeta) {
      themeMeta.setAttribute("content", light ? "#f3f3f7" : "#1c1c1e");
    }
    if (themeButton) {
      themeButton.setAttribute("aria-pressed", light ? "true" : "false");
    }
    paintChart(true);
    syncChartColors();
  }

  if (themeButton) {
    themeButton.addEventListener("click", function () {
      var next = root.getAttribute("data-theme") === "light" ? "dark" : "light";
      applyTheme(next);
      try {
        localStorage.setItem("nst-site-theme", next);
      } catch (error) { /* 忽略 */ }
    });
  }

  if (darkQuery && darkQuery.addEventListener) {
    darkQuery.addEventListener("change", function (event) {
      var stored = null;
      try {
        stored = localStorage.getItem("nst-site-theme");
      } catch (error) { /* 忽略 */ }
      if (!stored) {
        applyTheme(event.matches ? "light" : "dark");
      }
    });
  }

  /* ---------- 3. 顶栏与移动导航 ---------- */
  var header = document.getElementById("siteHeader");
  var navToggle = document.getElementById("navToggle");
  var siteNav = document.getElementById("siteNav");
  var navLinks = siteNav ? siteNav.querySelectorAll('a[href^="#"]') : [];

  function setNavOpen(open) {
    if (!siteNav || !navToggle) {
      return;
    }
    siteNav.classList.toggle("is-open", open);
    navToggle.setAttribute("aria-expanded", open ? "true" : "false");
  }

  if (navToggle && siteNav) {
    navToggle.addEventListener("click", function () {
      setNavOpen(!siteNav.classList.contains("is-open"));
    });

    siteNav.addEventListener("click", function (event) {
      if (event.target.closest && event.target.closest("a")) {
        setNavOpen(false);
      }
    });

    document.addEventListener("keydown", function (event) {
      if (event.key === "Escape") {
        setNavOpen(false);
      }
    });

    document.addEventListener("click", function (event) {
      if (!siteNav.classList.contains("is-open")) {
        return;
      }
      if (siteNav.contains(event.target) || navToggle.contains(event.target)) {
        return;
      }
      setNavOpen(false);
    });

    var wideQuery = window.matchMedia ? window.matchMedia("(min-width: 901px)") : null;
    if (wideQuery && wideQuery.addEventListener) {
      wideQuery.addEventListener("change", function () {
        setNavOpen(false);
      });
    }
  }

  var lastScrolled = false;
  function syncHeader() {
    if (!header) {
      return;
    }
    var scrolled = (window.pageYOffset || document.documentElement.scrollTop || 0) > 8;
    if (scrolled !== lastScrolled) {
      header.classList.toggle("is-scrolled", scrolled);
      lastScrolled = scrolled;
    }
  }
  syncHeader();
  window.addEventListener("scroll", syncHeader, { passive: true });

  /* ---------- 4. 当前章节高亮 ---------- */
  var sectionLinks = {};
  Array.prototype.forEach.call(navLinks, function (link) {
    var id = link.getAttribute("href").slice(1);
    if (id) {
      sectionLinks[id] = link;
    }
  });

  var sections = document.querySelectorAll("main section[id]");
  function markActive(id) {
    Object.keys(sectionLinks).forEach(function (key) {
      var active = key === id;
      sectionLinks[key].classList.toggle("is-active", active);
      if (active) {
        sectionLinks[key].setAttribute("aria-current", "location");
      } else {
        sectionLinks[key].removeAttribute("aria-current");
      }
    });
  }

  if ("IntersectionObserver" in window && sections.length) {
    var visible = {};
    var sectionObserver = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        visible[entry.target.id] = entry.isIntersecting ? entry.intersectionRatio : 0;
      });
      var bestId = "";
      var bestRatio = 0;
      Object.keys(visible).forEach(function (key) {
        if (visible[key] > bestRatio) {
          bestRatio = visible[key];
          bestId = key;
        }
      });
      if (bestId) {
        markActive(bestId);
      }
    }, { rootMargin: "-40% 0px -45% 0px", threshold: [0, 0.25, 0.5, 0.75, 1] });

    Array.prototype.forEach.call(sections, function (section) {
      sectionObserver.observe(section);
    });

    Array.prototype.forEach.call(navLinks, function (link) {
      link.addEventListener("click", function () {
        markActive(link.getAttribute("href").slice(1));
      });
    });
  }

  /* ---------- 4.5 顶栏液态玻璃（hyalite） ---------- */
  var navGlass = document.getElementById("navGlass");
  if (navGlass) {
    var glassMounted = false;
    try {
      if (window.Hyalite && window.Hyalite.supported()) {
        window.Hyalite.attach(navGlass, {
          bevel: 18,
          thickness: 16,
          slope: 2.2,
          blur: 6,
          dispersion: 1.1,
          shade: 0.42,
          rim: 1.25,
          edgeW: 7,
          sat: 0.92,
          light: -100,
          materialize: 0,
          settle: 0
        });
        glassMounted = true;
      }
    } catch (error) {
      // 能力嗅探通过但滤镜不可用时，保留 CSS 霜化兜底，不打断页面。
      glassMounted = false;
    }
    navGlass.setAttribute("data-glass", glassMounted ? "hyalite" : "fallback");
  }

  /* ---------- 5. 滚动显现与计数器 ---------- */
  var reveals = document.querySelectorAll(".reveal");
  var counters = document.querySelectorAll(".counter");

  function finishReveal() {
    Array.prototype.forEach.call(reveals, function (item) {
      item.classList.add("is-visible");
    });
  }

  function animateCounter(node) {
    var target = parseFloat(node.getAttribute("data-target"));
    if (isNaN(target)) {
      return;
    }
    if (reduceMotion) {
      node.textContent = String(target);
      return;
    }
    var start = null;
    var duration = 1400;
    function step(timestamp) {
      if (start === null) {
        start = timestamp;
      }
      var progress = Math.min((timestamp - start) / duration, 1);
      var eased = 1 - Math.pow(1 - progress, 3);
      node.textContent = String(Math.round(target * eased));
      if (progress < 1) {
        window.requestAnimationFrame(step);
      }
    }
    node.textContent = "0";
    window.requestAnimationFrame(step);
  }

  if (reduceMotion || !("IntersectionObserver" in window)) {
    finishReveal();
    Array.prototype.forEach.call(counters, function (node) {
      node.textContent = node.getAttribute("data-target") || node.textContent;
    });
  } else {
    var revealObserver = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) {
          entry.target.classList.add("is-visible");
          revealObserver.unobserve(entry.target);
        }
      });
    }, { rootMargin: "0px 0px -8% 0px", threshold: 0.08 });

    Array.prototype.forEach.call(reveals, function (item) {
      revealObserver.observe(item);
    });

    var counterObserver = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) {
          animateCounter(entry.target);
          counterObserver.unobserve(entry.target);
        }
      });
    }, { threshold: 0.6 });

    Array.prototype.forEach.call(counters, function (node) {
      node.textContent = "0";
      counterObserver.observe(node);
    });
  }

  /* ---------- 6. 模拟波形演示 ---------- */
  var canvas = document.getElementById("speedChart");
  var demoDownload = document.getElementById("demoDownload");
  var demoUpload = document.getElementById("demoUpload");
  var demoTotal = document.getElementById("demoTotal");
  var demoElapsed = document.getElementById("demoElapsed");

  var chartState = {
    points: 90,
    download: [],
    upload: [],
    scaleDownload: 640,
    scaleUpload: 260,
    colors: null,
    timer: null,
    tickTimer: null,
    started: Date.now()
  };

  function readColors() {
    var style = window.getComputedStyle(root);
    return {
      download: style.getPropertyValue("--download").trim() || "#4cc2ff",
      upload: style.getPropertyValue("--upload").trim() || "#ff7ac3",
      grid: style.getPropertyValue("--border").trim() || "#3a3a40",
      text: style.getPropertyValue("--text-3").trim() || "#a0a0a8"
    };
  }

  function syncChartColors() {
    chartState.colors = readColors();
  }

  function seedSeries() {
    var download = [];
    var upload = [];
    for (var i = 0; i < chartState.points; i += 1) {
      download.push(0.58 + Math.sin(i / 7) * 0.06 + Math.random() * 0.05);
      upload.push(0.24 + Math.sin(i / 9) * 0.04 + Math.random() * 0.03);
    }
    chartState.download = download;
    chartState.upload = upload;
  }

  function pushSample() {
    var lastDownload = chartState.download[chartState.download.length - 1] || 0.6;
    var lastUpload = chartState.upload[chartState.upload.length - 1] || 0.24;
    var nextDownload = Math.min(0.92, Math.max(0.32, lastDownload + (Math.random() - 0.48) * 0.07));
    var nextUpload = Math.min(0.4, Math.max(0.1, lastUpload + (Math.random() - 0.5) * 0.05));
    chartState.download.push(nextDownload);
    chartState.upload.push(nextUpload);
    if (chartState.download.length > chartState.points) {
      chartState.download.shift();
      chartState.upload.shift();
    }

    var downloadRate = nextDownload * chartState.scaleDownload;
    var uploadRate = nextUpload * chartState.scaleUpload;
    if (demoDownload) {
      demoDownload.textContent = downloadRate.toFixed(2);
    }
    if (demoUpload) {
      demoUpload.textContent = uploadRate.toFixed(2);
    }
    if (demoTotal) {
      demoTotal.textContent = (downloadRate + uploadRate).toFixed(2);
    }
    paintChart(false);
  }

  function paintChart(force) {
    if (!canvas || !canvas.getContext) {
      return;
    }
    var context = canvas.getContext("2d");
    var width = canvas.clientWidth || 640;
    var height = canvas.clientHeight || 172;
    var dpr = Math.min(window.devicePixelRatio || 1, 2);
    if (canvas.width !== Math.round(width * dpr) || canvas.height !== Math.round(height * dpr)) {
      canvas.width = Math.round(width * dpr);
      canvas.height = Math.round(height * dpr);
    }
    context.setTransform(dpr, 0, 0, dpr, 0, 0);
    context.clearRect(0, 0, width, height);

    var colors = chartState.colors || readColors();
    var padding = 12;
    var plotWidth = width - padding * 2;
    var plotHeight = height - padding * 2;

    context.strokeStyle = colors.grid;
    context.globalAlpha = 0.35;
    context.lineWidth = 1;
    for (var row = 0; row <= 3; row += 1) {
      var y = padding + (plotHeight / 3) * row;
      context.beginPath();
      context.moveTo(padding, y + 0.5);
      context.lineTo(width - padding, y + 0.5);
      context.stroke();
    }
    context.globalAlpha = 1;

    function drawSeries(series, color, fill) {
      if (!series.length) {
        return;
      }
      var step = plotWidth / Math.max(series.length - 1, 1);
      context.beginPath();
      series.forEach(function (value, index) {
        var x = padding + index * step;
        var y = padding + plotHeight * (1 - Math.min(Math.max(value, 0), 1));
        if (index === 0) {
          context.moveTo(x, y);
        } else {
          context.lineTo(x, y);
        }
      });
      context.strokeStyle = color;
      context.lineWidth = 2;
      context.lineJoin = "round";
      context.lineCap = "round";
      context.stroke();

      if (fill) {
        context.lineTo(padding + (series.length - 1) * step, height - padding);
        context.lineTo(padding, height - padding);
        context.closePath();
        var gradient = context.createLinearGradient(0, padding, 0, height - padding);
        gradient.addColorStop(0, color);
        gradient.addColorStop(1, "rgba(0,0,0,0)");
        context.globalAlpha = 0.16;
        context.fillStyle = gradient;
        context.fill();
        context.globalAlpha = 1;
      }
    }

    drawSeries(chartState.download, colors.download, true);
    drawSeries(chartState.upload, colors.upload, false);

    // 均值参考线
    var average = 0.62;
    var averageY = padding + plotHeight * (1 - average);
    context.save();
    context.setLineDash([4, 5]);
    context.strokeStyle = colors.text;
    context.globalAlpha = 0.7;
    context.lineWidth = 1;
    context.beginPath();
    context.moveTo(padding, averageY + 0.5);
    context.lineTo(width - padding, averageY + 0.5);
    context.stroke();
    context.restore();

    if (force) {
      return;
    }
  }

  function formatElapsed(milliseconds) {
    var total = Math.floor(milliseconds / 1000);
    var minutes = Math.floor(total / 60);
    var seconds = total % 60;
    return (minutes < 10 ? "0" + minutes : String(minutes)) + ":" + (seconds < 10 ? "0" + seconds : String(seconds));
  }

  function startDemo() {
    seedSeries();
    syncChartColors();
    var sample = chartState.download[chartState.download.length - 1];
    var upload = chartState.upload[chartState.upload.length - 1];
    if (demoDownload) {
      demoDownload.textContent = (sample * chartState.scaleDownload).toFixed(2);
    }
    if (demoUpload) {
      demoUpload.textContent = (upload * chartState.scaleUpload).toFixed(2);
    }
    if (demoTotal) {
      demoTotal.textContent = ((sample * chartState.scaleDownload) + (upload * chartState.scaleUpload)).toFixed(2);
    }
    paintChart(true);

    if (reduceMotion) {
      if (demoElapsed) {
        demoElapsed.textContent = "00:00";
      }
      return;
    }

    chartState.tickTimer = window.setInterval(pushSample, 700);
    chartState.timer = window.setInterval(function () {
      if (demoElapsed) {
        demoElapsed.textContent = formatElapsed(Date.now() - chartState.started);
      }
    }, 1000);
  }

  function stopDemo() {
    if (chartState.tickTimer) {
      window.clearInterval(chartState.tickTimer);
      chartState.tickTimer = null;
    }
    if (chartState.timer) {
      window.clearInterval(chartState.timer);
      chartState.timer = null;
    }
  }

  if (canvas) {
    startDemo();
    var resizeTimer = null;
    window.addEventListener("resize", function () {
      if (resizeTimer) {
        window.clearTimeout(resizeTimer);
      }
      resizeTimer = window.setTimeout(function () {
        syncChartColors();
        paintChart(true);
      }, 150);
    });
    document.addEventListener("visibilitychange", function () {
      if (document.hidden) {
        stopDemo();
      } else if (!reduceMotion) {
        chartState.started = Date.now() - 0;
        startDemo();
      }
    });
  }

  /* ---------- 7. 复制邮箱 ---------- */
  var toastNode = document.getElementById("toast");
  var toastTimer = null;
  var copyButton = document.getElementById("copyEmailBtn");

  function showToast(message) {
    if (!toastNode || !message) {
      return;
    }
    toastNode.textContent = message;
    toastNode.classList.add("is-visible");
    if (toastTimer) {
      window.clearTimeout(toastTimer);
    }
    toastTimer = window.setTimeout(function () {
      toastNode.classList.remove("is-visible");
    }, 2200);
  }

  function toastText(key, fallback) {
    var value = translate(key);
    return currentLang() === "en" ? (value || fallback) : fallback;
  }

  if (copyButton) {
    copyButton.addEventListener("click", function () {
      var email = copyButton.getAttribute("data-email") || "";
      function done() {
        showToast(toastText("toast.copied", "邮箱已复制"));
      }
      function failed() {
        showToast(toastText("toast.copyFailed", "复制失败，请手动复制"));
      }
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(email).then(done, failed);
      } else {
        try {
          var helper = document.createElement("textarea");
          helper.value = email;
          helper.setAttribute("readonly", "");
          helper.style.position = "fixed";
          helper.style.opacity = "0";
          document.body.appendChild(helper);
          helper.select();
          document.execCommand("copy");
          document.body.removeChild(helper);
          done();
        } catch (error) {
          failed();
        }
      }
    });
  }

  /* ---------- 8. 页脚年份 ---------- */
  var yearNode = document.getElementById("year");
  if (yearNode) {
    yearNode.textContent = String(new Date().getFullYear());
  }

  /* ---------- 9. 外链安全属性 ---------- */
  Array.prototype.forEach.call(document.querySelectorAll('a[target="_blank"]'), function (link) {
    var rel = link.getAttribute("rel") || "";
    if (rel.indexOf("noopener") === -1) {
      link.setAttribute("rel", (rel + " noopener noreferrer").trim());
    }
  });
})();
