(function () {
    'use strict';
    window.BF = window.BF || {};
    var overlay, body, btnImages, btnDocs, btnConfirm, btnCancel, btnClose;
    var modeToggle, subtitle, captionInput;
    var currentFiles = [];
    var mode = 'images';
    var onSendCallback = null;

    function init() {
        overlay = document.getElementById('attachOverlay');
        body = document.getElementById('attachDialogBody');
        btnImages = document.getElementById('attachAsImages');
        btnDocs = document.getElementById('attachAsDocs');
        btnConfirm = document.getElementById('attachConfirm');
        btnCancel = document.getElementById('attachCancel');
        btnClose = document.getElementById('attachDialogClose');
        modeToggle = document.getElementById('attachModeToggle');
        subtitle = document.getElementById('attachDialogSubtitle');
        captionInput = document.getElementById('attachCaption');
        btnClose.addEventListener('click', close);
        btnCancel.addEventListener('click', close);
        overlay.addEventListener('click', function (e) { if (e.target === overlay) close(); });
        btnImages.addEventListener('click', function () { setMode('images'); render(); });
        btnDocs.addEventListener('click', function () { setMode('docs'); render(); });
        btnConfirm.addEventListener('click', submit);
        captionInput.addEventListener('input', autosizeCaption);
        captionInput.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); submit(); }
            if (e.key === 'Escape') { e.preventDefault(); close(); }
        });
    }

    var IMAGE_EXTS = ['jpg','jpeg','png','gif','webp','bmp','avif','heic','heif','tiff','tif','svg','ico'];
    function isImageFile(f) {
        if (f.type && f.type.startsWith('image/')) return true;
        var ext = (f.name || '').split('.').pop().toLowerCase();
        return IMAGE_EXTS.indexOf(ext) !== -1;
    }

    function formatSize(bytes) {
        if (bytes < 1024) return bytes + ' Б';
        if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' КБ';
        if (bytes < 1024 * 1024 * 1024) return (bytes / 1024 / 1024).toFixed(1) + ' МБ';
        return (bytes / 1024 / 1024 / 1024).toFixed(2) + ' ГБ';
    }

    function getFileExt(name) {
        var idx = (name || '').lastIndexOf('.');
        if (idx < 0 || idx === name.length - 1) return 'FILE';
        return name.substring(idx + 1).toUpperCase().substring(0, 4);
    }

    function open(files, onSend, prefillText) {
        onSendCallback = onSend;
        currentFiles = Array.from(files).map(function (f) {
            return { file: f, isImage: isImageFile(f), previewUrl: null };
        });
        var hasAnyImage = currentFiles.some(function (f) { return f.isImage; });
        var hasNonImage = currentFiles.some(function (f) { return !f.isImage; });
        mode = hasNonImage ? 'docs' : 'images';
        btnImages.style.display = hasAnyImage ? '' : 'none';
        btnDocs.style.display = '';
        modeToggle.style.display = hasAnyImage ? '' : 'none';
        captionInput.value = prefillText || '';
        autosizeCaption();
        render();
        overlay.classList.add('visible');
        setTimeout(function () { captionInput.focus(); }, 50);
    }

    function close() {
        overlay.classList.remove('visible');
        currentFiles.forEach(function (f) { if (f.previewUrl) URL.revokeObjectURL(f.previewUrl); });
        currentFiles = [];
        captionInput.value = '';
        captionInput.style.height = 'auto';
        btnConfirm.disabled = false;
        onSendCallback = null;
    }

    function setMode(m) {
        mode = m;
        btnImages.classList.toggle('active', m === 'images');
        btnDocs.classList.toggle('active', m === 'docs');
    }

    function updateSubtitle() {
        var n = currentFiles.length;
        var word = n === 1 ? 'файл' : (n >= 2 && n <= 4 ? 'файла' : 'файлов');
        subtitle.textContent = n + ' ' + word;
    }

    function autosizeCaption() {
        captionInput.style.height = 'auto';
        captionInput.style.height = Math.min(captionInput.scrollHeight, 140) + 'px';
    }

    function buildRemoveButton(item) {
        var rm = document.createElement('button');
        rm.className = 'attach-remove';
        rm.type = 'button';
        rm.title = 'Удалить';
        rm.textContent = '\xd7';
        rm.addEventListener('click', function (e) {
            e.stopPropagation();
            // Вычисляем актуальный индекс в момент клика, а не захваченный
            // (splice сдвигает массив после первого удаления).
            var actualIdx = currentFiles.indexOf(item);
            if (actualIdx !== -1) {
                if (item.previewUrl) URL.revokeObjectURL(item.previewUrl);
                currentFiles.splice(actualIdx, 1);
            }
            if (currentFiles.length === 0) { close(); return; }
            var hasAnyImage = currentFiles.some(function (f) { return f.isImage; });
            var hasNonImage = currentFiles.some(function (f) { return !f.isImage; });
            mode = hasNonImage ? 'docs' : 'images';
            btnImages.style.display = hasAnyImage ? '' : 'none';
            btnDocs.style.display = '';
            modeToggle.style.display = hasAnyImage ? '' : 'none';
            render();
        });
        return rm;
    }

    function render() {
        body.innerHTML = '';
        updateSubtitle();
        var asGrid = mode === 'images';
        var container = document.createElement('div');
        container.className = asGrid ? 'attach-grid' : 'attach-list';
        currentFiles.forEach(function (item) {
            var div = document.createElement('div');
            div.className = 'attach-preview-item';
            if (asGrid && item.isImage) {
                if (!item.previewUrl) item.previewUrl = URL.createObjectURL(item.file);
                var img = document.createElement('img');
                img.src = item.previewUrl;
                img.alt = item.file.name;
                img.style.cursor = 'pointer';
                img.title = 'Редактировать';
                img.addEventListener('click', function (e) {
                    e.stopPropagation();
                    if (!window.BF.imageEditor) return;
                    BF.imageEditor.open(item.file, function (newFile) {
                        if (!newFile) return;
                        if (item.previewUrl) { URL.revokeObjectURL(item.previewUrl); item.previewUrl = null; }
                        item.file = newFile;
                        item.isImage = true;
                        render();
                    });
                });
                div.appendChild(img);
            } else {
                var row = document.createElement('div');
                row.className = 'attach-doc-row';
                var icon = document.createElement('div');
                icon.className = 'attach-doc-icon';
                icon.textContent = getFileExt(item.file.name);
                var info = document.createElement('div');
                info.className = 'attach-doc-info';
                var nm = document.createElement('div');
                nm.className = 'attach-doc-name';
                nm.textContent = item.file.name;
                nm.title = item.file.name;
                var sz = document.createElement('div');
                sz.className = 'attach-doc-size';
                sz.textContent = formatSize(item.file.size);
                info.appendChild(nm);
                info.appendChild(sz);
                row.appendChild(icon);
                row.appendChild(info);
                div.appendChild(row);
            }
            div.appendChild(buildRemoveButton(item));
            container.appendChild(div);
        });
        body.appendChild(container);
        setMode(mode);
    }

    function submit() {
        if (currentFiles.length === 0) return;
        btnConfirm.disabled = true;
        var asDocuments = mode === 'docs';
        var caption = captionInput.value.trim();
        // Оригинальные файлы отправляются без клиентской конвертации:
        // canvas.drawImage теряет ICC-профили → оранжевый/жёлтый сдвиг цвета.
        // Конвертация (JPEG 85%, ресайз) выполняется сервером (ImageSharp), как в AdminPanel.
        var outFiles = currentFiles.map(function (item) { return item.file; });
        var cb = onSendCallback;
        close();
        if (cb) cb(outFiles, asDocuments, caption);
    }

    window.BF.attach = { init: init, open: open };
})();
