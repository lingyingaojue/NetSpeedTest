/* ============================================================
   NetSpeedTest — 官方网站交互脚本（无第三方依赖）
   ============================================================ */

(function () {
  "use strict";

  /* 脚本加载成功后再启用“滚动显现”，避免脚本失败时页面空白 */
  document.documentElement.classList.add("js");

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

      card.addEventListener("mouseleave", function () {
        card.style.setProperty("--mx", "50%");
        card.style.setProperty("--my", "50%");
      });
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
      ctx.fillText("均值 " + avgDownload.toFixed(0), padding.left + 6, avgY - 3);
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
