// Гироскоп для параллакса (автор 07.09): deviceorientation → наклон в осях экрана с учётом ориентации.
// iOS 13+ требует разрешение из жеста пользователя — просим на первом тапе по странице.
mergeInto(LibraryManager.library, {
  SwGyroInit: function () {
    if (window.__swGyro) return;
    var g = window.__swGyro = { x: 0, y: 0, has: 0 };
    function onOri(e) {
      if (e.gamma == null || e.beta == null) return;
      var a = (screen.orientation && typeof screen.orientation.angle === 'number') ? screen.orientation.angle : (window.orientation || 0);
      var tx, ty;
      switch (a) {
        case 90: tx = e.beta; ty = -e.gamma; break;
        case 270: case -90: tx = -e.beta; ty = e.gamma; break;
        case 180: tx = -e.gamma; ty = -e.beta; break;
        default: tx = e.gamma; ty = e.beta;
      }
      g.x = tx; g.y = ty; g.has = 1;
    }
    function start() { window.addEventListener('deviceorientation', onOri, true); }
    var D = window.DeviceOrientationEvent;
    if (D && typeof D.requestPermission === 'function') {
      var ask = function () {
        document.removeEventListener('touchend', ask); document.removeEventListener('click', ask);
        D.requestPermission().then(function (s) { if (s === 'granted') start(); }).catch(function () {});
      };
      document.addEventListener('touchend', ask, { passive: true });
      document.addEventListener('click', ask);
    } else start();
  },
  SwGyroHas: function () { return window.__swGyro && window.__swGyro.has ? 1 : 0; },
  SwGyroX: function () { return window.__swGyro ? window.__swGyro.x : 0; },
  SwGyroY: function () { return window.__swGyro ? window.__swGyro.y : 0; }
  ,
  // --- полный экран (автор 07.09): запрос из жеста; если браузер отклонил (вне жеста), повторяем на следующем тапе
  SwFullscreen: function () {
    var el = document.documentElement;
    function req() {
      var f = el.requestFullscreen || el.webkitRequestFullscreen || el.mozRequestFullScreen;
      if (!f) return false;
      try { var p = f.call(el, { navigationUI: 'hide' }); if (p && p.catch) p.catch(function () { arm(); }); } catch (e) { arm(); }
      try { if (screen.orientation && screen.orientation.lock) screen.orientation.lock('portrait').catch(function () {}); } catch (e) {}
      return true;
    }
    var armed = false;
    function arm() {
      if (armed) return; armed = true;
      var once = function () { document.removeEventListener('touchend', once); document.removeEventListener('click', once); armed = false; req(); };
      document.addEventListener('touchend', once, { passive: true }); document.addEventListener('click', once);
    }
    req();
  },
  SwCanFullscreen: function () {
    try {
      var mm = window.matchMedia;
      if (window.navigator.standalone === true) return 0;
      if (mm && (mm('(display-mode: standalone)').matches || mm('(display-mode: fullscreen)').matches)) return 0;
      if (document.fullscreenElement || document.webkitFullscreenElement) return 0;
      var el = document.documentElement;
      return (el.requestFullscreen || el.webkitRequestFullscreen || el.mozRequestFullScreen) ? 1 : 0;
    } catch (e) { return 0; }
  },
  SwIsStandalone: function () { try { var mm = window.matchMedia; return (window.navigator.standalone === true || (mm && (mm('(display-mode: standalone)').matches || mm('(display-mode: fullscreen)').matches)) || document.fullscreenElement || document.webkitFullscreenElement) ? 1 : 0; } catch (e) { return 0; } },
  SwIsIOS: function () { return /iPhone|iPad|iPod/i.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1) ? 1 : 0; },
  // безопасные отступы (чёлка) в CSS px: left, right, top, bottom → массив из 4 float
  SwSafeInsets: function (outPtr) {
    var v = [0, 0, 0, 0];
    try {
      var d = window.__swSafeDiv;
      if (!d) {
        d = window.__swSafeDiv = document.createElement('div');
        d.style.cssText = 'position:fixed;left:0;top:0;width:0;height:0;visibility:hidden;pointer-events:none;' +
          'padding-left:env(safe-area-inset-left);padding-right:env(safe-area-inset-right);padding-top:env(safe-area-inset-top);padding-bottom:env(safe-area-inset-bottom)';
        document.body.appendChild(d);
      }
      var cs = getComputedStyle(d);
      v = [parseFloat(cs.paddingLeft) || 0, parseFloat(cs.paddingRight) || 0, parseFloat(cs.paddingTop) || 0, parseFloat(cs.paddingBottom) || 0];
    } catch (e) {}
    for (var i = 0; i < 4; i++) HEAPF32[(outPtr >> 2) + i] = v[i] * (window.devicePixelRatio || 1);
  }
});
