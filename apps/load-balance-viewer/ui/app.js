(() => {
  'use strict';

  const $ = (id) => document.getElementById(id);
  const clamp = (v, min, max) => Math.max(min, Math.min(max, v));
  const fmtSigned = (v, d = 3) => Number.isFinite(v) ? `${v >= 0 ? '+' : ''}${v.toFixed(d)}` : '—';
  const fmtKg = (v) => `${Math.max(0, Number(v) || 0).toFixed(1)} kg`;
  const fmtPct = (v) => Number.isFinite(v) ? `${v.toFixed(1)}%` : '—';

  let currentMode = 'real';
  let lastAlertSerial = -1;
  let dragging = false;

  const post = (message) => {
    if (window.chrome?.webview) window.chrome.webview.postMessage(message);
  };

  function setModeUi(mode) {
    currentMode = mode;
    $('modeReal').classList.toggle('active', mode === 'real');
    $('modeMock').classList.toggle('active', mode === 'mock');
    $('mockControls').hidden = mode !== 'mock';
    $('connectButton').hidden = mode !== 'real';
  }

  function render(s) {
    setModeUi(s.mode || 'real');
    $('connectionText').textContent = s.connectionText || 'Not connected';
    const dot = $('statusDot');
    dot.className = `status-dot ${s.connectionState || 'idle'}`;
    $('connectButton').textContent = s.connected ? '接続済み' : 'Wii Balance Boardを接続';

    $('lfKg').textContent = fmtKg(s.lf); $('rfKg').textContent = fmtKg(s.rf);
    $('lbKg').textContent = fmtKg(s.lb); $('rbKg').textContent = fmtKg(s.rb);
    $('totalKg').textContent = fmtKg(s.total);

    const maxBarKg = 45;
    $('lfBar').style.width = `${clamp((s.lf || 0) / maxBarKg * 100, 0, 100)}%`;
    $('rfBar').style.width = `${clamp((s.rf || 0) / maxBarKg * 100, 0, 100)}%`;
    $('lbBar').style.width = `${clamp((s.lb || 0) / maxBarKg * 100, 0, 100)}%`;
    $('rbBar').style.width = `${clamp((s.rb || 0) / maxBarKg * 100, 0, 100)}%`;

    $('leftPct').textContent = fmtPct(s.leftPct); $('rightPct').textContent = fmtPct(s.rightPct);
    $('frontPct').textContent = fmtPct(s.frontPct); $('backPct').textContent = fmtPct(s.backPct);
    $('lrMarker').style.left = `${Number.isFinite(s.rightPct) ? clamp(s.rightPct, 0, 100) : 50}%`;
    $('fbMarker').style.left = `${Number.isFinite(s.backPct) ? clamp(s.backPct, 0, 100) : 50}%`;

    $('copX').textContent = fmtSigned(s.copX); $('copY').textContent = fmtSigned(s.copY);
    $('relX').textContent = fmtSigned(s.relativeX); $('relY').textContent = fmtSigned(s.relativeY);

    $('weightPresence').textContent = s.weightPresent ? 'WEIGHT DETECTED' : 'NO WEIGHT';
    $('weightPresence').classList.toggle('on', !!s.weightPresent);
    $('noWeightOverlay').hidden = !!s.weightPresent;
    $('copDot').hidden = !s.weightPresent;

    if (s.weightPresent && Number.isFinite(s.copX) && Number.isFinite(s.copY)) {
      $('copDot').style.left = `${clamp((s.copX + 1) / 2 * 100, 5, 95)}%`;
      $('copDot').style.top = `${clamp((1 - s.copY) / 2 * 100, 5, 95)}%`;
    }

    const marker = $('centerMarker');
    marker.hidden = !s.centerApplied;
    if (s.centerApplied && Number.isFinite(s.centerX) && Number.isFinite(s.centerY)) {
      marker.style.left = `${clamp((s.centerX + 1) / 2 * 100, 5, 95)}%`;
      marker.style.top = `${clamp((1 - s.centerY) / 2 * 100, 5, 95)}%`;
    }

    $('zeroState').textContent = s.zeroState || 'Not set';
    $('centerState').textContent = s.centerState || 'Not set';
    $('logState').textContent = s.logState || 'Stopped';
    $('logButton').textContent = s.logging ? '■ LOG STOP' : '● LOG START';
    $('logButton').classList.toggle('logging', !!s.logging);

    if (s.alertSerial !== lastAlertSerial && s.alertText) {
      lastAlertSerial = s.alertSerial;
      showAlert(s.alertText, s.alertType || 'warn');
    }
  }

  let alertTimer = null;
  function showAlert(text, type = 'warn') {
    const box = $('alertBox');
    box.textContent = text; box.hidden = false;
    box.style.borderColor = type === 'ok' ? '#285c4a' : '#76532f';
    box.style.background = type === 'ok' ? '#0e2b22' : '#2a1c0d';
    box.style.color = type === 'ok' ? '#8be4ba' : '#ffd8a7';
    clearTimeout(alertTimer);
    alertTimer = setTimeout(() => box.hidden = true, 6500);
  }

  window.chrome?.webview?.addEventListener('message', (event) => {
    const data = event.data;
    if (data?.type === 'state') render(data);
  });

  $('modeReal').addEventListener('click', () => post({ type: 'mode', mode: 'real' }));
  $('modeMock').addEventListener('click', () => post({ type: 'mode', mode: 'mock' }));
  $('connectButton').addEventListener('click', () => post({ type: 'connect' }));
  $('zeroButton').addEventListener('click', () => post({ type: 'zero' }));
  $('centerButton').addEventListener('click', () => post({ type: 'center' }));
  $('logButton').addEventListener('click', () => post({ type: 'toggleLog' }));
  $('openLogsButton').addEventListener('click', () => post({ type: 'openLogs' }));

  $('mockWeight').addEventListener('input', (e) => {
    const value = Number(e.target.value);
    $('mockWeightValue').textContent = fmtKg(value);
    post({ type: 'mockWeight', value });
  });

  $('mockCenterButton').addEventListener('click', () => post({ type: 'mockPose', x: 0, y: 0 }));

  const board = $('copBoard');
  const moveMock = (e) => {
    if (!dragging || currentMode !== 'mock') return;
    const r = board.getBoundingClientRect();
    const x = clamp(((e.clientX - r.left) / r.width) * 2 - 1, -.88, .88);
    const y = clamp(1 - ((e.clientY - r.top) / r.height) * 2, -.88, .88);
    post({ type: 'mockPose', x, y });
  };

  board.addEventListener('pointerdown', (e) => {
    if (currentMode !== 'mock') return;
    dragging = true; board.setPointerCapture?.(e.pointerId); moveMock(e);
  });
  board.addEventListener('pointermove', moveMock);
  board.addEventListener('pointerup', () => dragging = false);
  board.addEventListener('pointercancel', () => dragging = false);

  post({ type: 'ready' });
})();
