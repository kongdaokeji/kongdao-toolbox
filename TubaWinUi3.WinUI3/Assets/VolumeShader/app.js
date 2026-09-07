/* 毒蘑菇测试 - WebGL 体素分形压力测试
 * 渲染引擎基于 volumeshader-bm (livcm) 的光线步进分形算法重写
 */
(function () {
    'use strict';

    // ==================== 三档压力模式 ====================
    // scale: 内部渲染分辨率倍率(相对屏幕物理像素), >1 即超出屏幕分辨率
    // stp:   光线步进间距(越小负载越大)
    // maxK:  最大步数(每像素光线追踪迭代上限)
    // solver: 二分求根迭代次数
    // kiter: 分形公式嵌套迭代次数
    var MODES = {
        easy: {
            label: '轻松',
            scale: 0.5,
            stp: 0.0040,
            maxK: 500,
            solver: 6,
            kiter: 3
        },
        medium: {
            label: '中等',
            scale: 1.0,
            stp: 0.0020,
            maxK: 1002,
            solver: 8,
            kiter: 5
        },
        insane: {
            label: '变态',
            scale: 2.5,
            stp: 0.0012,
            maxK: 1500,
            solver: 12,
            kiter: 7
        }
    };

    var settings = {
        mode: 'medium',
        scale: 1.0,
        stp: 0.002,
        maxK: 1002,
        solver: 8,
        kiter: 5
    };

    var autoRotate = true;
    var showFps = true;
    var paused = false;

    // ==================== 相机状态 ====================
    var ang1 = 2.8, ang2 = 0.4;   // 方位角 / 俯仰角
    var len = 1.6;                 // 观察距离
    var cenx = 0, ceny = 0, cenz = 0; // 平移中心

    // 鼠标状态
    var ml = 0, mr = 0, mm = 0;
    var mx = 0, my = 0;

    // ==================== DOM ====================
    var canvas = document.getElementById('gl-canvas');
    var elFpsNow = document.getElementById('fps-now');
    var elFpsMeta = document.getElementById('fps-meta');
    var elFpsRes = document.getElementById('fps-res');
    var elFpsCard = document.getElementById('fps-card');
    var elGpuInfo = document.getElementById('gpu-info');
    var elError = document.getElementById('gl-error');

    // ==================== WebGL 初始化 ====================
    var gl = canvas.getContext('webgl', {
        antialias: false,
        alpha: false,
        depth: false,
        stencil: false,
        powerPreference: 'high-performance'
    }) || canvas.getContext('experimental-webgl');

    if (!gl) {
        elError.hidden = false;
        elError.textContent = '无法初始化 WebGL，请确认浏览器已启用硬件加速。';
        return;
    }

    var maxViewport = gl.getParameter(gl.MAX_VIEWPORT_DIMS);
    var maxSide = Math.min(maxViewport[0], maxViewport[1]);

    function getGpuName() {
        try {
            var dbg = gl.getExtension('WEBGL_debug_renderer_info');
            if (dbg) {
                return gl.getParameter(dbg.UNMASKED_RENDERER_WEBGL);
            }
        } catch (e) { }
        return gl.getParameter(gl.RENDERER);
    }

    // ==================== 着色器 ====================
    var VSHADER = [
        'attribute vec4 position;',
        'varying vec3 dir, localdir;',
        'uniform vec3 right, forward, up, origin;',
        'uniform float x, y;',
        'void main() {',
        '   gl_Position = position;',
        '   dir = forward + right * position.x * x + up * position.y * y;',
        '   localdir.x = position.x * x;',
        '   localdir.y = position.y * y;',
        '   localdir.z = -1.0;',
        '}'
    ].join('\n');

    var FSHADER = [
        'precision highp float;',
        '#define PI 3.14159265358979324',
        '#define M_L 0.3819660113',
        '#define M_R 0.6180339887',
        '#define MAXR 8',
        '#define SOLVER %SOLVER%',
        '#define MAXK %MAXK%',
        '#define STP %STP%',
        '#define KITER %KITER%',
        '',
        'float kernel(vec3 ver);',
        '',
        'float kernel(vec3 ver)',
        '{',
        '    vec3 a;',
        '    float b, c, d, e;',
        '    a = ver;',
        '    for (int i = 0; i < KITER; i++)',
        '    {',
        '        b = length(a);',
        '        c = atan(a.y, a.x) * 8.0;',
        '        e = 1.0 / b;',
        '        d = acos(clamp(a.z / b, -1.0, 1.0)) * 8.0;',
        '        b = pow(b, 8.0);',
        '        a = vec3(b * sin(d) * cos(c), b * sin(d) * sin(c), b * cos(d)) + ver;',
        '        if (b > 6.0)',
        '            break;',
        '    }',
        '    return 4.0 - a.x * a.x - a.y * a.y - a.z * a.z;',
        '}',
        '',
        'uniform vec3 right, forward, up, origin;',
        'varying vec3 dir, localdir;',
        'uniform float len;',
        '',
        'vec3 ver;',
        'int hit;',
        'float v, v1, v2;',
        'float r1, r2, r3, r4, m1, m2, m3, m4;',
        'vec3 n, refl;',
        'const float stp = STP;',
        'vec3 color;',
        '',
        'void main()',
        '{',
        '    color.r = 0.0;',
        '    color.g = 0.0;',
        '    color.b = 0.0;',
        '    hit = 0;',
        '    v1 = kernel(origin + dir * (stp * len));',
        '    v2 = kernel(origin);',
        '    for (int k = 2; k < MAXK; k++)',
        '    {',
        '        ver = origin + dir * (stp * len * float(k));',
        '        v = kernel(ver);',
        '        if (v > 0.0 && v1 < 0.0)',
        '        {',
        '            r1 = stp * len * float(k - 1);',
        '            r2 = stp * len * float(k);',
        '            m1 = kernel(origin + dir * r1);',
        '            m2 = kernel(origin + dir * r2);',
        '            for (int l = 0; l < SOLVER; l++)',
        '            {',
        '                r3 = r1 * 0.5 + r2 * 0.5;',
        '                m3 = kernel(origin + dir * r3);',
        '                if (m3 > 0.0) { r2 = r3; m2 = m3; }',
        '                else { r1 = r3; m1 = m3; }',
        '            }',
        '            if (r3 < 2.0 * len) { hit = 1; break; }',
        '        }',
        '        if (v < v1 && v1 > v2 && v1 < 0.0 && (v1 * 2.0 > v || v1 * 2.0 > v2))',
        '        {',
        '            r1 = stp * len * float(k - 2);',
        '            r2 = stp * len * (float(k) - 2.0 + 2.0 * M_L);',
        '            r3 = stp * len * (float(k) - 2.0 + 2.0 * M_R);',
        '            r4 = stp * len * float(k);',
        '            m2 = kernel(origin + dir * r2);',
        '            m3 = kernel(origin + dir * r3);',
        '            for (int l = 0; l < MAXR; l++)',
        '            {',
        '                if (m2 > m3)',
        '                {',
        '                    r4 = r3; r3 = r2;',
        '                    r2 = r4 * M_L + r1 * M_R;',
        '                    m3 = m2;',
        '                    m2 = kernel(origin + dir * r2);',
        '                }',
        '                else',
        '                {',
        '                    r1 = r2; r2 = r3;',
        '                    r3 = r4 * M_R + r1 * M_L;',
        '                    m2 = m3;',
        '                    m3 = kernel(origin + dir * r3);',
        '                }',
        '            }',
        '            if (m2 > 0.0)',
        '            {',
        '                r1 = stp * len * float(k - 2);',
        '                m1 = kernel(origin + dir * r1);',
        '                m2 = kernel(origin + dir * r2);',
        '                for (int l = 0; l < SOLVER; l++)',
        '                {',
        '                    r3 = r1 * 0.5 + r2 * 0.5;',
        '                    m3 = kernel(origin + dir * r3);',
        '                    if (m3 > 0.0) { r2 = r3; m2 = m3; }',
        '                    else { r1 = r3; m1 = m3; }',
        '                }',
        '                if (r3 < 2.0 * len && r3 > stp * len) { hit = 1; break; }',
        '            }',
        '            else if (m3 > 0.0)',
        '            {',
        '                r1 = stp * len * float(k - 2);',
        '                r2 = r3;',
        '                m1 = kernel(origin + dir * r1);',
        '                m2 = kernel(origin + dir * r2);',
        '                for (int l = 0; l < SOLVER; l++)',
        '                {',
        '                    r3 = r1 * 0.5 + r2 * 0.5;',
        '                    m3 = kernel(origin + dir * r3);',
        '                    if (m3 > 0.0) { r2 = r3; m2 = m3; }',
        '                    else { r1 = r3; m1 = m3; }',
        '                }',
        '                if (r3 < 2.0 * len && r3 > stp * len) { hit = 1; break; }',
        '            }',
        '        }',
        '        v2 = v1;',
        '        v1 = v;',
        '    }',
        '    if (hit == 1)',
        '    {',
        '        ver = origin + dir * r3;',
        '        r1 = ver.x * ver.x + ver.y * ver.y + ver.z * ver.z;',
        '        n.x = kernel(ver - right * (r3 * 0.00025)) - kernel(ver + right * (r3 * 0.00025));',
        '        n.y = kernel(ver - up * (r3 * 0.00025)) - kernel(ver + up * (r3 * 0.00025));',
        '        n.z = kernel(ver + forward * (r3 * 0.00025)) - kernel(ver - forward * (r3 * 0.00025));',
        '        r3 = n.x * n.x + n.y * n.y + n.z * n.z;',
        '        n = n * (1.0 / sqrt(r3));',
        '        ver = localdir;',
        '        r3 = ver.x * ver.x + ver.y * ver.y + ver.z * ver.z;',
        '        ver = ver * (1.0 / sqrt(r3));',
        '        refl = n * (-2.0 * dot(ver, n)) + ver;',
        '        r3 = refl.x * 0.276 + refl.y * 0.920 + refl.z * 0.276;',
        '        r4 = n.x * 0.276 + n.y * 0.920 + n.z * 0.276;',
        '        r3 = max(0.0, r3);',
        '        r3 = r3 * r3 * r3 * r3;',
        '        r3 = r3 * 0.45 + r4 * 0.25 + 0.3;',
        '        n.x = sin(r1 * 10.0) * 0.5 + 0.5;',
        '        n.y = sin(r1 * 10.0 + 2.05) * 0.5 + 0.5;',
        '        n.z = sin(r1 * 10.0 - 2.05) * 0.5 + 0.5;',
        '        color = n * r3;',
        '    }',
        '    gl_FragColor = vec4(color.x, color.y, color.z, 1.0);',
        '}'
    ].join('\n');

    var program = null;
    var uX, uY, uLen, uOrigin, uRight, uUp, uForward;
    var aPosition;
    var buffer = null;
    var positions = new Float32Array([
        -1.0, -1.0, 0.0, 1.0, -1.0, 0.0, 1.0, 1.0, 0.0,
        -1.0, -1.0, 0.0, 1.0, 1.0, 0.0, -1.0, 1.0, 0.0
    ]);

    function compileShader(type, source) {
        var sh = gl.createShader(type);
        gl.shaderSource(sh, source);
        gl.compileShader(sh);
        if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) {
            throw new Error(gl.getShaderInfoLog(sh) || '着色器编译失败');
        }
        return sh;
    }

    function buildProgram() {
        var fsh = FSHADER
            .replace('%SOLVER%', String(settings.solver))
            .replace('%MAXK%', String(settings.maxK))
            .replace('%STP%', settings.stp.toFixed(4))
            .replace('%KITER%', String(settings.kiter));

        var vs = compileShader(gl.VERTEX_SHADER, VSHADER);
        var fs = compileShader(gl.FRAGMENT_SHADER, fsh);

        var prog = gl.createProgram();
        gl.attachShader(prog, vs);
        gl.attachShader(prog, fs);
        gl.linkProgram(prog);
        if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) {
            throw new Error(gl.getProgramInfoLog(prog) || '程序链接失败');
        }
        gl.useProgram(prog);

        if (!buffer) {
            buffer = gl.createBuffer();
            gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
            gl.bufferData(gl.ARRAY_BUFFER, positions, gl.STATIC_DRAW);
        }
        gl.bindBuffer(gl.ARRAY_BUFFER, buffer);

        aPosition = gl.getAttribLocation(prog, 'position');
        gl.enableVertexAttribArray(aPosition);
        gl.vertexAttribPointer(aPosition, 3, gl.FLOAT, false, 0, 0);

        uX = gl.getUniformLocation(prog, 'x');
        uY = gl.getUniformLocation(prog, 'y');
        uLen = gl.getUniformLocation(prog, 'len');
        uOrigin = gl.getUniformLocation(prog, 'origin');
        uRight = gl.getUniformLocation(prog, 'right');
        uUp = gl.getUniformLocation(prog, 'up');
        uForward = gl.getUniformLocation(prog, 'forward');

        program = prog;
    }

    // ==================== 固定分辨率画布 ====================
    // 统一 720p 基准 (1280×720 × 模式倍率)，不随窗口/DPI 变化，保证跨设备可比
    var BASE_W = 1280, BASE_H = 720;
    var renderW = 0, renderH = 0;

    function resizeCanvas() {
        var w = Math.round(BASE_W * settings.scale);
        var h = Math.round(BASE_H * settings.scale);
        var k = Math.min(1, maxSide / Math.max(w, h));
        w = Math.round(w * k);
        h = Math.round(h * k);
        w -= w % 2;
        h -= h % 2;
        w = Math.max(w, 2);
        h = Math.max(h, 2);

        if (canvas.width !== w || canvas.height !== h) {
            canvas.width = w;
            canvas.height = h;
        }
        renderW = w;
        renderH = h;
        gl.viewport(0, 0, w, h);
        updateResText();
    }

    // ==================== 渲染 ====================
    function draw() {
        var w = renderW, h = renderH;
        var x = w * 2.0 / (w + h);
        var y = h * 2.0 / (w + h);

        gl.uniform1f(uX, x);
        gl.uniform1f(uY, y);
        gl.uniform1f(uLen, len);
        gl.uniform3f(uOrigin,
            len * Math.cos(ang1) * Math.cos(ang2) + cenx,
            len * Math.sin(ang2) + ceny,
            len * Math.sin(ang1) * Math.cos(ang2) + cenz);
        gl.uniform3f(uRight, Math.sin(ang1), 0, -Math.cos(ang1));
        gl.uniform3f(uUp, -Math.sin(ang2) * Math.cos(ang1), Math.cos(ang2), -Math.sin(ang2) * Math.sin(ang1));
        gl.uniform3f(uForward, -Math.cos(ang1) * Math.cos(ang2), -Math.sin(ang2), -Math.sin(ang1) * Math.cos(ang2));

        gl.drawArrays(gl.TRIANGLES, 0, 6);
        gl.finish();
    }

    // ==================== 帧率统计 ====================
    var fpsFrames = 0, fpsTime = 0;
    var fpsCurrent = 0, fpsAvg = 0, fpsMin = Infinity, fpsSamples = 0, fpsSum = 0;

    function resetFps() {
        fpsFrames = 0;
        fpsTime = 0;
        fpsCurrent = 0;
        fpsAvg = 0;
        fpsMin = Infinity;
        fpsSamples = 0;
        fpsSum = 0;
    }

    function updateFpsHud() {
        var v = Math.round(fpsCurrent);
        elFpsNow.textContent = String(v);
        elFpsNow.style.color = fpsCurrent >= 55 ? 'var(--fps-good)' :
            fpsCurrent >= 30 ? 'var(--fps-mid)' : 'var(--fps-bad)';
        elFpsMeta.textContent = paused
            ? '已暂停'
            : (fpsCurrent > 0 ? (1000 / fpsCurrent).toFixed(1) : '--') + ' ms · 平均 ' +
              (fpsAvg > 0 ? fpsAvg.toFixed(0) : '--') +
              ' · 最低 ' + (fpsMin === Infinity ? '--' : fpsMin.toFixed(0));
    }

    function updateResText() {
        elFpsRes.textContent = '720p 基准 ×' + settings.scale.toFixed(1) +
            ' = ' + renderW + '×' + renderH;
    }

    // ==================== 主循环 ====================
    var lastTime = performance.now();
    var rafId = 0;

    function loop(now) {
        rafId = requestAnimationFrame(loop);
        var dt = (now - lastTime) / 1000;
        lastTime = now;

        if (!paused) {
            if (autoRotate) {
                ang1 += dt * 0.5;
            }
            draw();
            fpsFrames++;
            fpsTime += dt;
            if (fpsTime >= 0.5 && fpsFrames >= 3) {
                fpsCurrent = fpsFrames / fpsTime;
                fpsFrames = 0;
                fpsTime = 0;
                fpsSum += fpsCurrent;
                fpsSamples++;
                fpsAvg = fpsSum / fpsSamples;
                if (fpsCurrent < fpsMin) fpsMin = fpsCurrent;
                updateFpsHud();
            }
        }
    }

    // WebGL 上下文丢失保护:GPU 驱动重置时自动重建着色器并恢复循环
    canvas.addEventListener('webglcontextlost', function (e) {
        e.preventDefault();
        paused = true;
        updateFpsHud();
    }, false);

    canvas.addEventListener('webglcontextrestored', function () {
        try {
            buildProgram();
        } catch (ex) {
            elError.hidden = false;
            elError.textContent = 'GPU 上下文恢复失败: ' + ex.message;
            return;
        }
        paused = false;
        glyphPause.innerHTML = '&#xE769;';
        resetFps();
        resizeCanvas();
        rafId = requestAnimationFrame(loop);
    }, false);

    // ==================== 交互 ====================
    function onPointerDown(e) {
        canvas.setPointerCapture(e.pointerId);
        if (e.button === 0) { ml = 1; mm = 0; }
        if (e.button === 2) { mr = 1; mm = 0; }
        mx = e.clientX;
        my = e.clientY;
    }

    function onPointerMove(e) {
        if (ml === 1) {
            ang1 += (e.clientX - mx) * 0.002;
            ang2 += (e.clientY - my) * 0.002;
            if (e.clientX !== mx || e.clientY !== my) mm = 1;
        }
        if (mr === 1) {
            var l = len * 4.0 / (renderW + renderH);
            cenx += l * (-(e.clientX - mx) * Math.sin(ang1) - (e.clientY - my) * Math.sin(ang2) * Math.cos(ang1));
            ceny += l * ((e.clientY - my) * Math.cos(ang2));
            cenz += l * ((e.clientX - mx) * Math.cos(ang1) - (e.clientY - my) * Math.sin(ang2) * Math.sin(ang1));
            if (e.clientX !== mx || e.clientY !== my) mm = 1;
        }
        mx = e.clientX;
        my = e.clientY;
    }

    function onPointerUp(e) {
        if (e.button === 0) ml = 0;
        if (e.button === 2) mr = 0;
        if (canvas.hasPointerCapture(e.pointerId)) canvas.releasePointerCapture(e.pointerId);
    }

    function onWheel(e) {
        e.preventDefault();
        len *= Math.exp(-0.001 * e.deltaY);
    }

    function resetView() {
        ang1 = 2.8;
        ang2 = 0.4;
        len = 1.6;
        cenx = 0; ceny = 0; cenz = 0;
    }

    canvas.addEventListener('pointerdown', onPointerDown);
    canvas.addEventListener('pointermove', onPointerMove);
    canvas.addEventListener('pointerup', onPointerUp);
    canvas.addEventListener('pointercancel', onPointerUp);
    canvas.addEventListener('wheel', onWheel, { passive: false });
    canvas.addEventListener('contextmenu', function (e) {
        if (mm === 1) e.preventDefault();
    });

    // 分辨率固定为 720p 基准，不随窗口大小变化

    // ==================== UI 绑定 ====================
    var flyout = document.getElementById('settings-flyout');
    var flyoutMask = document.getElementById('flyout-mask');
    var btnSettings = document.getElementById('btn-settings');
    var btnClose = document.getElementById('btn-close');
    var btnDone = document.getElementById('btn-done');
    var btnPause = document.getElementById('btn-pause');
    var glyphPause = document.getElementById('glyph-pause');
    var btnResetView = document.getElementById('btn-reset-view');
    var btnResetView2 = document.getElementById('btn-reset-view2');
    var sldScale = document.getElementById('sld-scale');
    var sldStep = document.getElementById('sld-step');
    var sldMaxK = document.getElementById('sld-maxk');
    var sldKiter = document.getElementById('sld-kiter');
    var swAuto = document.getElementById('sw-auto');
    var swFps = document.getElementById('sw-fps');

    function openFlyout() {
        flyout.hidden = false;
        flyoutMask.hidden = false;
    }

    function closeFlyout() {
        flyout.hidden = true;
        flyoutMask.hidden = true;
    }

    btnSettings.addEventListener('click', openFlyout);
    btnClose.addEventListener('click', closeFlyout);
    btnDone.addEventListener('click', closeFlyout);
    flyoutMask.addEventListener('click', closeFlyout);

    btnPause.addEventListener('click', function () {
        paused = !paused;
        glyphPause.innerHTML = paused ? '&#xE768;' : '&#xE769;';
        if (!paused) {
            lastTime = performance.now();
            resetFps();
            rafId = requestAnimationFrame(loop);
        } else {
            updateFpsHud();
        }
    });

    function onResetView() {
        resetView();
    }
    btnResetView.addEventListener('click', onResetView);
    btnResetView2.addEventListener('click', onResetView);

    // 滑杆填充
    function paintRange(input) {
        var min = parseFloat(input.min);
        var max = parseFloat(input.max);
        var pct = ((parseFloat(input.value) - min) / (max - min)) * 100;
        input.style.setProperty('--range-fill', pct + '%');
    }

    var rebuildTimer = 0;
    function scheduleRebuild() {
        clearTimeout(rebuildTimer);
        rebuildTimer = setTimeout(function () {
            try {
                buildProgram();
            } catch (e) {
                console.error(e);
            }
        }, 120);
    }

    function applySettings() {
        resizeCanvas();
        scheduleRebuild();
        resetFps();
        updateResText();
    }

    // 滑杆
    sldScale.addEventListener('input', function () {
        settings.scale = parseInt(sldScale.value, 10) / 100;
        document.getElementById('val-scale').textContent = settings.scale.toFixed(1) + '×';
        paintRange(sldScale);
        applySettings();
    });

    sldStep.addEventListener('input', function () {
        settings.stp = parseInt(sldStep.value, 10) / 10000;
        document.getElementById('val-step').textContent = settings.stp.toFixed(4);
        paintRange(sldStep);
        applySettings();
    });

    sldMaxK.addEventListener('input', function () {
        settings.maxK = parseInt(sldMaxK.value, 10);
        document.getElementById('val-maxk').textContent = String(settings.maxK);
        paintRange(sldMaxK);
        applySettings();
    });

    sldKiter.addEventListener('input', function () {
        settings.kiter = parseInt(sldKiter.value, 10);
        document.getElementById('val-kiter').textContent = String(settings.kiter);
        paintRange(sldKiter);
        applySettings();
    });

    swAuto.addEventListener('change', function () {
        autoRotate = swAuto.checked;
    });

    swFps.addEventListener('change', function () {
        showFps = swFps.checked;
        elFpsCard.style.display = showFps ? '' : 'none';
    });

    // 压力模式切换
    var modeBar = document.getElementById('mode-bar');
    modeBar.addEventListener('click', function (e) {
        var btn = e.target.closest('.mode-btn');
        if (!btn) return;
        var mode = btn.dataset.mode;
        if (!MODES[mode]) return;
        setMode(mode);
    });

    function setMode(mode) {
        var p = MODES[mode];
        settings.mode = mode;
        settings.scale = p.scale;
        settings.stp = p.stp;
        settings.maxK = p.maxK;
        settings.solver = p.solver;
        settings.kiter = p.kiter;

        document.querySelectorAll('.mode-btn').forEach(function (b) {
            b.classList.toggle('active', b.dataset.mode === mode);
        });

        syncSliders();
        applySettings();
    }

    function syncSliders() {
        sldScale.value = String(Math.round(settings.scale * 100));
        sldStep.value = String(Math.round(settings.stp * 10000));
        sldMaxK.value = String(settings.maxK);
        sldKiter.value = String(settings.kiter);
        document.getElementById('val-scale').textContent = settings.scale.toFixed(1) + '×';
        document.getElementById('val-step').textContent = settings.stp.toFixed(4);
        document.getElementById('val-maxk').textContent = String(settings.maxK);
        document.getElementById('val-kiter').textContent = String(settings.kiter);
        paintRange(sldScale);
        paintRange(sldStep);
        paintRange(sldMaxK);
        paintRange(sldKiter);
    }

    // ==================== 启动 ====================
    try {
        buildProgram();
    } catch (e) {
        elError.hidden = false;
        elError.textContent = '着色器初始化失败: ' + e.message;
        return;
    }

    elGpuInfo.textContent = getGpuName();
    resetView();
    resizeCanvas();
    syncSliders();
    resetFps();
    updateFpsHud();
    updateResText();

    rafId = requestAnimationFrame(loop);
})();
