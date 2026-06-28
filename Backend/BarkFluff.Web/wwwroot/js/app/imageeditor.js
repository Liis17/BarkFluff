(function () {
    'use strict';
    window.BF = window.BF || {};

    // Ограничение рабочего разрешения, чтобы не съедать память на огромных фото.
    // Сервер (ImageSharp) всё равно делает финальный ресайз/сжатие.
    var MAX_DIM = 4096;

    var overlay, stage, wrap, displayCanvas, ctx, cropFrame;
    var colorInput, colorWrap, sizeInput, sizeWrap, sizeLabel;

    var state = {
        fileName: 'image.jpg',
        img: null,
        imgIsBitmap: false,
        objUrl: null,
        rotate: 0,            // 0 | 90 | 180 | 270, по часовой
        flipH: false,
        flipV: false,
        srcW: 0, srcH: 0,     // рабочие размеры источника (после MAX_DIM)
        baseCanvas: null, baseCtx: null,     // фото после трансформаций
        drawCanvas: null, drawCtx: null,     // слой штрихов (кисть/мозаика/ластик)
        pixelatedCanvas: null,               // мозаичная версия base (лениво)
        bw: 0, bh: 0,         // натуральные размеры текущего кадра
        view: { dispW: 0, dispH: 0, scale: 1 },
        tool: 'crop',         // 'crop' | 'brush' | 'pixelate' | 'eraser'
        brushColor: '#000000',
        brushSize: 24,        // диаметр в дисплейных px
        crop: null,           // {x,y,w,h} в дисплейных px относительно displayCanvas
        onResult: null
    };

    var drawing = false;
    var lastPt = null;
    var cropDragging = null;

    function byId(id) { return document.getElementById(id); }
    function clamp(v, min, max) { return Math.max(min, Math.min(max, v)); }

    // ===== init =====

    function init() {
        overlay = byId('imgEditorOverlay');
        if (!overlay) return;
        stage = byId('ieStage');
        wrap = byId('ieCanvasWrap');
        displayCanvas = byId('ieCanvas');
        ctx = displayCanvas.getContext('2d');
        cropFrame = byId('ieCropFrame');

        byId('ieCrop').onclick = function () { setTool('crop'); };
        byId('ieRotateLeft').onclick = rotateLeft;
        byId('ieRotateRight').onclick = rotateRight;
        byId('ieFlipH').onclick = flipHorizontal;
        byId('ieFlipV').onclick = flipVertical;
        byId('ieBrush').onclick = function () { setTool('brush'); };
        byId('iePixelate').onclick = function () { setTool('pixelate'); };
        byId('ieEraser').onclick = function () { setTool('eraser'); };

        colorWrap = byId('ieColorWrap');
        colorInput = byId('ieColor');
        colorInput.value = state.brushColor;
        colorInput.oninput = function () { state.brushColor = this.value; };

        sizeWrap = byId('ieSizeWrap');
        sizeInput = byId('ieSize');
        sizeLabel = byId('ieSizeVal');
        sizeInput.value = state.brushSize;
        sizeInput.oninput = function () {
            state.brushSize = parseInt(this.value, 10) || 24;
            updateSizeLabel();
        };
        updateSizeLabel();

        byId('ieCancel').onclick = function () { close(null); };
        byId('ieApply').onclick = exportFile;
        byId('ieEditorClose').onclick = function () { close(null); };
        overlay.addEventListener('click', function (e) { if (e.target === overlay) close(null); });

        bindDrawing();
        bindCropDrag();
        window.addEventListener('resize', onResize);
        document.addEventListener('keydown', onKey);
    }

    function updateSizeLabel() {
        if (sizeLabel) sizeLabel.textContent = state.brushSize + ' px';
    }

    function onKey(e) {
        if (!overlay || !overlay.classList.contains('visible')) return;
        if (e.key === 'Escape') { e.preventDefault(); close(null); }
    }

    var resizeTimer = null;
    function onResize() {
        if (!overlay || !overlay.classList.contains('visible')) return;
        if (resizeTimer) clearTimeout(resizeTimer);
        resizeTimer = setTimeout(function () {
            recomputeView();
            if (state.tool === 'crop') initCropFrame();
            renderDisplay();
        }, 120);
    }

    // ===== open / close =====

    function open(file, onResult) {
        if (!overlay) return;
        state.onResult = onResult || null;
        state.fileName = (file && file.name) || 'image.jpg';
        state.rotate = 0; state.flipH = false; state.flipV = false;
        state.crop = null;

        loadImage(file, function (img, w, h) {
            if (!img) { close(null); return; }
            state.img = img;
            var sf = Math.min(1, MAX_DIM / Math.max(w, h));
            state.srcW = Math.max(1, Math.round(w * sf));
            state.srcH = Math.max(1, Math.round(h * sf));

            state.baseCanvas = document.createElement('canvas');
            state.baseCtx = state.baseCanvas.getContext('2d');
            rebakeBase();

            state.drawCanvas = document.createElement('canvas');
            state.drawCanvas.width = state.bw;
            state.drawCanvas.height = state.bh;
            state.drawCtx = state.drawCanvas.getContext('2d');
            state.pixelatedCanvas = null;

            overlay.classList.add('visible');
            requestAnimationFrame(function () {
                recomputeView();
                setTool('crop');
                renderDisplay();
            });
        });
    }

    function close(result) {
        overlay.classList.remove('visible');
        drawing = false; lastPt = null; cropDragging = null;
        if (state.objUrl) { URL.revokeObjectURL(state.objUrl); state.objUrl = null; }
        if (state.imgIsBitmap && state.img && state.img.close) {
            try { state.img.close(); } catch (_) { }
        }
        state.img = null; state.imgIsBitmap = false;
        state.baseCanvas = state.baseCtx = null;
        state.drawCanvas = state.drawCtx = null;
        state.pixelatedCanvas = null;
        var cb = state.onResult;
        state.onResult = null;
        if (cb) cb(result);
    }

    function loadImage(file, cb) {
        // createImageBitmap с imageOrientation учитывает EXIF-поворот фото с камеры.
        if (window.createImageBitmap) {
            createImageBitmap(file, { imageOrientation: 'from-image' }).then(function (bmp) {
                state.imgIsBitmap = true;
                cb(bmp, bmp.width, bmp.height);
            }).catch(function () { loadViaImg(file, cb); });
        } else {
            loadViaImg(file, cb);
        }
    }

    function loadViaImg(file, cb) {
        var url = URL.createObjectURL(file);
        state.objUrl = url;
        state.imgIsBitmap = false;
        var img = new Image();
        img.onload = function () { cb(img, img.naturalWidth, img.naturalHeight); };
        img.onerror = function () { cb(null, 0, 0); };
        img.src = url;
    }

    // ===== трансформации (rebake) =====

    function rebakeBase() {
        var W = state.srcW, H = state.srcH;
        var swap = (state.rotate === 90 || state.rotate === 270);
        state.bw = swap ? H : W;
        state.bh = swap ? W : H;
        var c = state.baseCanvas, x = state.baseCtx;
        c.width = state.bw; c.height = state.bh;
        x.setTransform(1, 0, 0, 1, 0, 0);
        x.clearRect(0, 0, state.bw, state.bh);
        x.save();
        x.translate(state.bw / 2, state.bh / 2);
        x.rotate(state.rotate * Math.PI / 180);
        x.scale(state.flipH ? -1 : 1, state.flipV ? -1 : 1);
        x.drawImage(state.img, -W / 2, -H / 2, W, H);
        x.restore();
    }

    // Слой штрихов нельзя пересобрать из оригинала — поворачиваем/отражаем его дельтой.
    function applyDeltaToDraw(dRot, fH, fV) {
        var oldC = state.drawCanvas;
        var tmp = document.createElement('canvas');
        tmp.width = state.bw; tmp.height = state.bh;
        var t = tmp.getContext('2d');
        t.save();
        t.translate(state.bw / 2, state.bh / 2);
        t.rotate(dRot * Math.PI / 180);
        t.scale(fH ? -1 : 1, fV ? -1 : 1);
        t.drawImage(oldC, -oldC.width / 2, -oldC.height / 2);
        t.restore();
        state.drawCanvas = tmp;
        state.drawCtx = t;
    }

    function afterTransform() {
        state.pixelatedCanvas = null;
        state.crop = null;
        recomputeView();
        if (state.tool === 'crop') initCropFrame();
        renderDisplay();
    }

    function rotateLeft() {
        state.rotate = (state.rotate + 270) % 360;
        rebakeBase();
        applyDeltaToDraw(270, false, false);
        afterTransform();
    }
    function rotateRight() {
        state.rotate = (state.rotate + 90) % 360;
        rebakeBase();
        applyDeltaToDraw(90, false, false);
        afterTransform();
    }
    function flipHorizontal() {
        state.flipH = !state.flipH;
        rebakeBase();
        applyDeltaToDraw(0, true, false);
        afterTransform();
    }
    function flipVertical() {
        state.flipV = !state.flipV;
        rebakeBase();
        applyDeltaToDraw(0, false, true);
        afterTransform();
    }

    // ===== отображение =====

    function recomputeView() {
        var availW = stage.clientWidth - 24;
        var availH = stage.clientHeight - 24;
        if (availW < 10) availW = stage.clientWidth || 320;
        if (availH < 10) availH = stage.clientHeight || 320;
        var ratio = Math.min(availW / state.bw, availH / state.bh, 1);
        if (!isFinite(ratio) || ratio <= 0) ratio = 1;
        var dispW = Math.max(1, Math.round(state.bw * ratio));
        var dispH = Math.max(1, Math.round(state.bh * ratio));
        state.view = { dispW: dispW, dispH: dispH, scale: state.bw / dispW };
        displayCanvas.width = dispW;
        displayCanvas.height = dispH;
        displayCanvas.style.width = dispW + 'px';
        displayCanvas.style.height = dispH + 'px';
        wrap.style.width = dispW + 'px';
        wrap.style.height = dispH + 'px';
    }

    function renderDisplay() {
        var dw = displayCanvas.width, dh = displayCanvas.height;
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        ctx.clearRect(0, 0, dw, dh);
        ctx.drawImage(state.baseCanvas, 0, 0, state.bw, state.bh, 0, 0, dw, dh);
        ctx.drawImage(state.drawCanvas, 0, 0, state.bw, state.bh, 0, 0, dw, dh);
    }

    // ===== инструменты =====

    function setActive(id, on) {
        var el = byId(id);
        if (el) el.classList.toggle('active', !!on);
    }

    function setTool(t) {
        state.tool = t;
        setActive('ieCrop', t === 'crop');
        setActive('ieBrush', t === 'brush');
        setActive('iePixelate', t === 'pixelate');
        setActive('ieEraser', t === 'eraser');

        var isDraw = (t !== 'crop');
        cropFrame.style.display = (t === 'crop') ? 'block' : 'none';
        if (t === 'crop') initCropFrame();
        displayCanvas.style.cursor = isDraw ? 'crosshair' : 'default';

        colorWrap.style.display = (t === 'brush') ? '' : 'none';
        sizeWrap.style.display = isDraw ? '' : 'none';
    }

    // ===== пикселизация =====

    function ensurePixelated() {
        if (state.pixelatedCanvas) return;
        var bw = state.bw, bh = state.bh;
        var f = Math.max(8, Math.round(Math.max(bw, bh) / 64));
        var sw = Math.max(1, Math.round(bw / f));
        var sh = Math.max(1, Math.round(bh / f));
        var small = document.createElement('canvas');
        small.width = sw; small.height = sh;
        var sc = small.getContext('2d');
        sc.imageSmoothingEnabled = true;
        sc.drawImage(state.baseCanvas, 0, 0, sw, sh);
        var pc = document.createElement('canvas');
        pc.width = bw; pc.height = bh;
        var pctx = pc.getContext('2d');
        pctx.imageSmoothingEnabled = false;
        pctx.drawImage(small, 0, 0, sw, sh, 0, 0, bw, bh);
        state.pixelatedCanvas = pc;
    }

    // ===== рисование =====

    function rectScale() {
        var rect = displayCanvas.getBoundingClientRect();
        return { rect: rect, sx: state.bw / rect.width, sy: state.bh / rect.height };
    }

    function stampAt(nx, ny, r) {
        var c = state.drawCtx;
        if (state.tool === 'eraser') {
            c.globalCompositeOperation = 'destination-out';
            c.beginPath(); c.arc(nx, ny, r, 0, Math.PI * 2); c.fill();
            c.globalCompositeOperation = 'source-over';
        } else if (state.tool === 'pixelate') {
            ensurePixelated();
            c.save();
            c.beginPath(); c.arc(nx, ny, r, 0, Math.PI * 2); c.clip();
            c.drawImage(state.pixelatedCanvas, 0, 0);
            c.restore();
        } else {
            c.globalCompositeOperation = 'source-over';
            c.fillStyle = state.brushColor;
            c.beginPath(); c.arc(nx, ny, r, 0, Math.PI * 2); c.fill();
        }
    }

    function strokeLine(a, b, r) {
        var dx = b.nx - a.nx, dy = b.ny - a.ny;
        var dist = Math.sqrt(dx * dx + dy * dy);
        var step = Math.max(1, r * 0.5);
        var n = Math.ceil(dist / step);
        for (var i = 1; i <= n; i++) {
            var t = i / n;
            stampAt(a.nx + dx * t, a.ny + dy * t, r);
        }
    }

    function bindDrawing() {
        displayCanvas.addEventListener('pointerdown', onDrawDown);
        window.addEventListener('pointermove', onDrawMove);
        window.addEventListener('pointerup', onDrawUp);
    }

    function onDrawDown(e) {
        if (state.tool === 'crop') return;
        if (e.button !== undefined && e.button !== 0) return;
        e.preventDefault();
        drawing = true;
        try { displayCanvas.setPointerCapture(e.pointerId); } catch (_) { }
        var rs = rectScale();
        var nx = (e.clientX - rs.rect.left) * rs.sx;
        var ny = (e.clientY - rs.rect.top) * rs.sy;
        var r = Math.max(0.5, state.brushSize * rs.sx / 2);
        lastPt = { nx: nx, ny: ny, r: r };
        stampAt(nx, ny, r);
        renderDisplay();
    }

    function onDrawMove(e) {
        if (!drawing) return;
        e.preventDefault();
        var rs = rectScale();
        var nx = (e.clientX - rs.rect.left) * rs.sx;
        var ny = (e.clientY - rs.rect.top) * rs.sy;
        var r = Math.max(0.5, state.brushSize * rs.sx / 2);
        strokeLine(lastPt, { nx: nx, ny: ny }, r);
        lastPt = { nx: nx, ny: ny, r: r };
        renderDisplay();
    }

    function onDrawUp() {
        if (!drawing) return;
        drawing = false;
        lastPt = null;
    }

    // ===== обрезка (свободная рамка) =====

    function setCropRect(l, t, w, h) {
        cropFrame.style.left = l + 'px';
        cropFrame.style.top = t + 'px';
        cropFrame.style.width = w + 'px';
        cropFrame.style.height = h + 'px';
        state.crop = { x: l, y: t, w: w, h: h };
    }

    function initCropFrame() {
        setCropRect(0, 0, state.view.dispW, state.view.dispH);
    }

    function bindCropDrag() {
        cropFrame.addEventListener('pointerdown', onCropDown);
        window.addEventListener('pointermove', onCropMove);
        window.addEventListener('pointerup', onCropUp);
    }

    function onCropDown(e) {
        if (state.tool !== 'crop') return;
        var target = e.target;
        var handle = (target.classList && target.classList.contains('ie-crop-handle'))
            ? target.dataset.handle : null;
        if (target !== cropFrame && !handle) return;
        e.preventDefault();
        cropDragging = {
            mode: handle ? 'resize-' + handle : 'move',
            startX: e.clientX, startY: e.clientY,
            origLeft: parseFloat(cropFrame.style.left) || 0,
            origTop: parseFloat(cropFrame.style.top) || 0,
            origW: parseFloat(cropFrame.style.width) || 0,
            origH: parseFloat(cropFrame.style.height) || 0
        };
        try { cropFrame.setPointerCapture(e.pointerId); } catch (_) { }
    }

    function onCropMove(e) {
        if (!cropDragging) return;
        e.preventDefault();
        var dx = e.clientX - cropDragging.startX;
        var dy = e.clientY - cropDragging.startY;
        var W = state.view.dispW, H = state.view.dispH, min = 24;
        var L = cropDragging.origLeft, T = cropDragging.origTop;
        var w = cropDragging.origW, h = cropDragging.origH;
        var nl = L, nt = T, nw = w, nh = h;
        switch (cropDragging.mode) {
            case 'move':
                nl = clamp(L + dx, 0, W - w);
                nt = clamp(T + dy, 0, H - h);
                break;
            case 'resize-br':
                nw = clamp(w + dx, min, W - L);
                nh = clamp(h + dy, min, H - T);
                break;
            case 'resize-bl':
                nw = clamp(w - dx, min, L + w);
                nl = L + w - nw;
                nh = clamp(h + dy, min, H - T);
                break;
            case 'resize-tr':
                nw = clamp(w + dx, min, W - L);
                nh = clamp(h - dy, min, T + h);
                nt = T + h - nh;
                break;
            case 'resize-tl':
                nw = clamp(w - dx, min, L + w);
                nl = L + w - nw;
                nh = clamp(h - dy, min, T + h);
                nt = T + h - nh;
                break;
        }
        setCropRect(nl, nt, nw, nh);
    }

    function onCropUp() { cropDragging = null; }

    // ===== экспорт =====

    function renameToJpg(name) {
        var dot = (name || '').lastIndexOf('.');
        var base = dot > 0 ? name.substring(0, dot) : (name || 'image');
        return base + '.jpg';
    }

    function exportFile() {
        if (!state.baseCanvas) { close(null); return; }
        var bw = state.bw, bh = state.bh;
        var sx, sy, sw, sh;
        if (state.crop) {
            var sc = bw / state.view.dispW;
            sx = clamp(state.crop.x * sc, 0, bw);
            sy = clamp(state.crop.y * sc, 0, bh);
            sw = clamp(state.crop.w * sc, 1, bw - sx);
            sh = clamp(state.crop.h * sc, 1, bh - sy);
        } else {
            sx = 0; sy = 0; sw = bw; sh = bh;
        }
        var out = document.createElement('canvas');
        out.width = Math.max(1, Math.round(sw));
        out.height = Math.max(1, Math.round(sh));
        var octx = out.getContext('2d');
        // JPEG не хранит альфу — заливаем белым, чтобы прозрачные области не стали чёрными.
        octx.fillStyle = '#ffffff';
        octx.fillRect(0, 0, out.width, out.height);
        octx.drawImage(state.baseCanvas, sx, sy, sw, sh, 0, 0, out.width, out.height);
        octx.drawImage(state.drawCanvas, sx, sy, sw, sh, 0, 0, out.width, out.height);
        out.toBlob(function (blob) {
            if (!blob) { close(null); return; }
            var f = new File([blob], renameToJpg(state.fileName), { type: 'image/jpeg' });
            close(f);
        }, 'image/jpeg', 0.92);
    }

    window.BF.imageEditor = { init: init, open: open };
})();
