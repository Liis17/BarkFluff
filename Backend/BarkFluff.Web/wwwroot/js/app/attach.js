(function () {
    'use strict';
    window.BF = window.BF || {};
    var overlay, body, btnImages, btnDocs, btnConfirm, btnClose;
    var currentFiles = [];
    var mode = 'images';
    var onSendCallback = null;

    function init() {
        overlay = document.getElementById('attachOverlay');
        body = document.getElementById('attachDialogBody');
        btnImages = document.getElementById('attachAsImages');
        btnDocs = document.getElementById('attachAsDocs');
        btnConfirm = document.getElementById('attachConfirm');
        btnClose = document.getElementById('attachDialogClose');
        btnClose.addEventListener('click', close);
        overlay.addEventListener('click', function (e) { if (e.target === overlay) close(); });
        btnImages.addEventListener('click', function () { setMode('images'); });
        btnDocs.addEventListener('click', function () { setMode('docs'); });
        btnConfirm.addEventListener('click', submit);
    }

    function open(files, onSend) {
        onSendCallback = onSend;
        currentFiles = Array.from(files).map(function (f) {
            var isImage = f.type && f.type.startsWith('image/') && !f.type.startsWith('image/gif');
            return { file: f, isImage: isImage, previewUrl: null };
        });
        var hasNonImage = currentFiles.some(function (f) { return !f.isImage; });
        mode = hasNonImage ? 'docs' : 'images';
        btnImages.style.display = hasNonImage ? 'none' : '';
        render();
        overlay.classList.add('visible');
    }

    function close() {
        overlay.classList.remove('visible');
        currentFiles.forEach(function (f) { if (f.previewUrl) URL.revokeObjectURL(f.previewUrl); });
        currentFiles = [];
    }

    function setMode(m) {
        mode = m;
        btnImages.classList.toggle('active', m === 'images');
        btnDocs.classList.toggle('active', m === 'docs');
    }

    function render() {
        body.innerHTML = '';
        currentFiles.forEach(function (item, idx) {
            var i = idx;
            var div = document.createElement('div');
            div.className = 'attach-preview-item';
            if (item.isImage) {
                if (!item.previewUrl) item.previewUrl = URL.createObjectURL(item.file);
                var img = document.createElement('img');
                img.src = item.previewUrl;
                div.appendChild(img);
            } else {
                var label = document.createElement('div');
                label.className = 'attach-doc-label';
                label.textContent = item.file.name;
                div.appendChild(label);
            }
            var rm = document.createElement('button');
            rm.className = 'attach-remove';
            rm.textContent = '\xd7';
            rm.addEventListener('click', function () {
                if (item.previewUrl) URL.revokeObjectURL(item.previewUrl);
                currentFiles.splice(i, 1);
                var hasNonImage = currentFiles.some(function (f) { return !f.isImage; });
                if (hasNonImage) { mode = 'docs'; btnImages.style.display = 'none'; }
                else { btnImages.style.display = ''; }
                if (currentFiles.length === 0) { close(); return; }
                render();
            });
            div.appendChild(rm);
            body.appendChild(div);
        });
        setMode(mode);
    }

    var MAX_DIM = 2000;

    // createImageBitmap корректно применяет ICC-профиль при декодировании.
    // canvas.getContext('2d') без явного colorSpace позволяет браузеру
    // самостоятельно управлять цветовым пространством при drawImage —
    // явный { colorSpace: 'srgb' } ломает этот pipeline в Chrome.
    function convertToJpeg(file) {
        return createImageBitmap(file).then(function (bitmap) {
            var w = bitmap.width;
            var h = bitmap.height;
            if (w > MAX_DIM || h > MAX_DIM) {
                var scale = MAX_DIM / Math.max(w, h);
                w = Math.round(w * scale);
                h = Math.round(h * scale);
            }
            var canvas = document.createElement('canvas');
            canvas.width = w;
            canvas.height = h;
            canvas.getContext('2d').drawImage(bitmap, 0, 0, w, h);
            bitmap.close();
            return new Promise(function (resolve, reject) {
                canvas.toBlob(function (blob) {
                    if (!blob) { reject(new Error('blob_failed')); return; }
                    var base = (file.name || 'image').replace(/\.[^.]+$/, '');
                    resolve(new File([blob], base + '.jpg', { type: 'image/jpeg' }));
                }, 'image/jpeg', 0.85);
            });
        });
    }

    function submit() {
        btnConfirm.disabled = true;
        var asDocuments = mode === 'docs';
        var prepare;
        if (asDocuments) {
            prepare = Promise.resolve(currentFiles.map(function (item) { return item.file; }));
        } else {
            prepare = Promise.all(currentFiles.map(function (item) {
                return item.isImage
                    ? convertToJpeg(item.file).catch(function () { return item.file; })
                    : Promise.resolve(item.file);
            }));
        }
        prepare.then(function (outFiles) {
            close();
            if (onSendCallback) onSendCallback(outFiles, asDocuments);
        }).finally(function () { btnConfirm.disabled = false; });
    }

    window.BF.attach = { init: init, open: open };
})();
