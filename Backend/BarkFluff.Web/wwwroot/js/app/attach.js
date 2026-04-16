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

    function submit() {
        btnConfirm.disabled = true;
        var asDocuments = mode === 'docs';
        // Оригинальные файлы отправляются без клиентской конвертации:
        // canvas.drawImage теряет ICC-профили → оранжевый/жёлтый сдвиг цвета.
        // Конвертация (JPEG 85%, ресайз) выполняется сервером (ImageSharp), как в AdminPanel.
        var outFiles = currentFiles.map(function (item) { return item.file; });
        close();
        btnConfirm.disabled = false;
        if (onSendCallback) onSendCallback(outFiles, asDocuments);
    }

    window.BF.attach = { init: init, open: open };
})();
