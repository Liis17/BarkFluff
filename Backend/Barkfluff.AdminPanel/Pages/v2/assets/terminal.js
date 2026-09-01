/*
 * Small stateful terminal renderer for the remote SSH console.
 * It is intentionally dependency-free: the admin panel is served as static HTML.
 */
(function (global) {
    const DEFAULT_FOREGROUND = '#f5eff7';
    const DEFAULT_BACKGROUND = '#1d1b20';
    const ANSI_COLORS = [
        '#000000', '#cd3131', '#0dbc79', '#e5e510',
        '#2472c8', '#bc3fbc', '#11a8cd', '#e5e5e5',
        '#666666', '#f14c4c', '#23d18b', '#f5f543',
        '#3b8eea', '#d670d6', '#29b8db', '#ffffff'
    ];

    function defaultStyle() {
        return {
            foreground: null,
            background: null,
            bold: false,
            dim: false,
            italic: false,
            underline: false,
            inverse: false,
            strike: false
        };
    }

    const PLAIN_STYLE = Object.freeze(defaultStyle());

    function copyStyle(style) {
        return { ...style };
    }

    function sameStyle(left, right) {
        return left.foreground === right.foreground
            && left.background === right.background
            && left.bold === right.bold
            && left.dim === right.dim
            && left.italic === right.italic
            && left.underline === right.underline
            && left.inverse === right.inverse
            && left.strike === right.strike;
    }

    function indexedColor(index) {
        if (index >= 0 && index < 16)
            return ANSI_COLORS[index];
        if (index >= 16 && index <= 231) {
            const value = index - 16;
            const red = Math.floor(value / 36);
            const green = Math.floor((value % 36) / 6);
            const blue = value % 6;
            const channel = component => component === 0 ? 0 : 55 + component * 40;
            return `rgb(${channel(red)}, ${channel(green)}, ${channel(blue)})`;
        }
        if (index >= 232 && index <= 255) {
            const value = 8 + (index - 232) * 10;
            return `rgb(${value}, ${value}, ${value})`;
        }
        return null;
    }

    class TerminalBuffer {
        constructor(options = {}) {
            this.columns = Math.max(1, options.columns || 120);
            this.maxRows = Math.max(100, options.maxRows || 2000);
            this.maxChars = Math.max(1000, options.maxChars || 200000);
            this.reset();
        }

        reset() {
            this.lines = [[]];
            this.cursorX = 0;
            this.cursorY = 0;
            this.savedCursor = { x: 0, y: 0 };
            this.style = defaultStyle();
            this.state = 'normal';
            this.sequence = '';
        }

        write(data) {
            if (typeof data !== 'string' || data.length === 0)
                return;

            for (let index = 0; index < data.length;) {
                const character = data[index];

                if (this.state === 'normal') {
                    index += this.consumeNormal(data, index);
                    continue;
                }

                if (this.state === 'escape') {
                    if (character >= '\x20' && character <= '\x2f') {
                        this.state = 'escape-intermediate';
                        index++;
                        continue;
                    }
                    this.consumeEscape(character);
                    index++;
                    continue;
                }

                if (this.state === 'escape-intermediate') {
                    if (character === '\x1b') this.state = 'escape';
                    else this.state = 'normal';
                    index++;
                    continue;
                }

                if (this.state === 'csi') {
                    if (character === '\x1b') {
                        this.state = 'escape';
                        this.sequence = '';
                        index++;
                        continue;
                    }
                    if (character >= '@' && character <= '~') {
                        this.sequence += character;
                        this.consumeCsi(this.sequence);
                        this.state = 'normal';
                        this.sequence = '';
                        index++;
                        continue;
                    }
                    this.sequence += character;
                    if (this.sequence.length > 128) {
                        this.state = 'normal';
                        this.sequence = '';
                    }
                    index++;
                    continue;
                }

                if (this.state === 'string') {
                    if (character === '\x07' || character === '\x9c') {
                        this.state = 'normal';
                    } else if (character === '\x1b') {
                        this.state = 'string-escape';
                    }
                    index++;
                    continue;
                }

                if (this.state === 'string-escape') {
                    this.state = character === '\\' ? 'normal' : 'string';
                    index++;
                }
            }

            this.trimScrollback();
        }

        toText() {
            return this.lines.map(line => this.lineText(line)).join('\n');
        }

        getCell(row, column) {
            return this.lines[row]?.[column] || null;
        }

        getLineRuns(row) {
            const line = this.lines[row] || [];
            const end = this.lineEnd(line);

            const runs = [];
            let currentStyle = null;
            let currentText = '';
            const pushRun = () => {
                if (currentText.length > 0)
                    runs.push({ text: currentText, style: currentStyle });
                currentText = '';
            };

            for (let column = 0; column < end; column++) {
                const cell = line[column];
                const style = cell?.style || PLAIN_STYLE;
                if (currentStyle === null || !sameStyle(currentStyle, style)) {
                    pushRun();
                    currentStyle = style;
                }
                currentText += cell?.character || ' ';
            }
            pushRun();
            return runs;
        }

        consumeNormal(data, index) {
            const character = data[index];
            const code = character.charCodeAt(0);

            if (character === '\x1b') {
                this.state = 'escape';
                this.sequence = '';
                return 1;
            }
            if (character === '\x9b') {
                this.state = 'csi';
                this.sequence = '';
                return 1;
            }
            if (character === '\x9d' || character === '\x90'
                || character === '\x98' || character === '\x9e' || character === '\x9f') {
                this.state = 'string';
                return 1;
            }
            if (character === '\n' || character === '\x0b' || character === '\x0c') {
                this.lineFeed();
                return 1;
            }
            if (character === '\r') {
                this.cursorX = 0;
                return 1;
            }
            if (character === '\b') {
                this.cursorX = Math.max(0, this.cursorX - 1);
                return 1;
            }
            if (character === '\t') {
                this.cursorX = Math.min(this.columns, this.cursorX + (8 - this.cursorX % 8));
                return 1;
            }
            if (code < 0x20 || code === 0x7f) {
                return 1;
            }

            const codePoint = data.codePointAt(index);
            const symbol = String.fromCodePoint(codePoint);
            this.putCharacter(symbol);
            return symbol.length;
        }

        consumeEscape(character) {
            if (character === '\x1b') {
                this.sequence = '';
                return;
            }
            if (character === '[') {
                this.state = 'csi';
                this.sequence = '';
                return;
            }
            if (character === ']' || character === 'P' || character === '^'
                || character === '_') {
                this.state = 'string';
                this.sequence = '';
                return;
            }

            this.state = 'normal';
            switch (character) {
                case '7':
                    this.saveCursor();
                    break;
                case '8':
                    this.restoreCursor();
                    break;
                case 'D':
                case 'E':
                    this.lineFeed();
                    if (character === 'E') this.cursorX = 0;
                    break;
                case 'M':
                    this.reverseIndex();
                    break;
                case 'c':
                    this.reset();
                    break;
            }
        }

        consumeCsi(sequence) {
            const final = sequence[sequence.length - 1];
            const body = sequence.slice(0, -1);
            const parameterText = (body.match(/^[0-?]*/)?.[0] || '').replace(/^[?>!<]+/, '');
            const parameters = parameterText.length === 0
                ? []
                : parameterText.split(';').map(value => value === '' ? 0 : Number.parseInt(value, 10));
            const parameter = (position, fallback = 1) => {
                const value = parameters[position];
                return Number.isFinite(value) && value > 0
                    ? Math.min(value, Math.max(this.columns, this.maxRows))
                    : fallback;
            };

            switch (final) {
                case 'A': this.cursorY = Math.max(0, this.cursorY - parameter(0)); break;
                case 'B': this.cursorY += parameter(0); this.ensureLine(this.cursorY); break;
                case 'C':
                case 'a': this.cursorX = Math.min(this.columns, this.cursorX + parameter(0)); break;
                case 'D': this.cursorX = Math.max(0, this.cursorX - parameter(0)); break;
                case 'E': this.cursorY += parameter(0); this.cursorX = 0; this.ensureLine(this.cursorY); break;
                case 'F': this.cursorY = Math.max(0, this.cursorY - parameter(0)); this.cursorX = 0; break;
                case 'G':
                case '`': this.cursorX = Math.max(0, Math.min(this.columns - 1, parameter(0) - 1)); break;
                case 'd': this.cursorY = Math.max(0, parameter(0) - 1); this.ensureLine(this.cursorY); break;
                case 'H':
                case 'f':
                    this.cursorY = Math.max(0, parameter(0) - 1);
                    this.cursorX = Math.max(0, Math.min(this.columns - 1, parameter(1) - 1));
                    this.ensureLine(this.cursorY);
                    break;
                case 'J': this.eraseDisplay(parameters[0] || 0); break;
                case 'K': this.eraseLine(parameters[0] || 0); break;
                case 'L': this.insertLines(parameter(0)); break;
                case 'M': this.deleteLines(parameter(0)); break;
                case 'P': this.deleteCharacters(parameter(0)); break;
                case '@': this.insertCharacters(parameter(0)); break;
                case 'X': this.eraseCharacters(parameter(0)); break;
                case 'S': this.scrollUp(parameter(0)); break;
                case 'T': this.scrollDown(parameter(0)); break;
                case 'm': this.applySgr(parameters.length > 0 ? parameters : [0]); break;
                case 's': this.saveCursor(); break;
                case 'u': this.restoreCursor(); break;
            }
        }

        applySgr(parameters) {
            let style = copyStyle(this.style);
            for (let index = 0; index < parameters.length; index++) {
                const code = parameters[index];
                if (!Number.isFinite(code)) continue;
                if (code === 0) { style = defaultStyle(); continue; }
                if (code === 1) { style.bold = true; continue; }
                if (code === 2) { style.dim = true; continue; }
                if (code === 3) { style.italic = true; continue; }
                if (code === 4) { style.underline = true; continue; }
                if (code === 7) { style.inverse = true; continue; }
                if (code === 9) { style.strike = true; continue; }
                if (code === 22) { style.bold = false; style.dim = false; continue; }
                if (code === 23) { style.italic = false; continue; }
                if (code === 24) { style.underline = false; continue; }
                if (code === 27) { style.inverse = false; continue; }
                if (code === 29) { style.strike = false; continue; }
                if (code === 39) { style.foreground = null; continue; }
                if (code === 49) { style.background = null; continue; }
                if (code >= 30 && code <= 37) { style.foreground = ANSI_COLORS[code - 30]; continue; }
                if (code >= 40 && code <= 47) { style.background = ANSI_COLORS[code - 40]; continue; }
                if (code >= 90 && code <= 97) { style.foreground = ANSI_COLORS[code - 90 + 8]; continue; }
                if (code >= 100 && code <= 107) { style.background = ANSI_COLORS[code - 100 + 8]; continue; }
                if (code === 38 || code === 48) {
                    const color = code === 38 ? 'foreground' : 'background';
                    if (parameters[index + 1] === 5 && Number.isFinite(parameters[index + 2])) {
                        style[color] = indexedColor(parameters[index + 2]);
                        index += 2;
                    } else if (parameters[index + 1] === 2 && parameters.slice(index + 2, index + 5).every(Number.isFinite)) {
                        const [red, green, blue] = parameters.slice(index + 2, index + 5)
                            .map(value => Math.max(0, Math.min(255, value)));
                        style[color] = `rgb(${red}, ${green}, ${blue})`;
                        index += 4;
                    }
                }
            }
            this.style = style;
        }

        putCharacter(character) {
            if (this.cursorX >= this.columns) {
                this.cursorX = 0;
                this.cursorY++;
            }
            const line = this.ensureLine(this.cursorY);
            line[this.cursorX] = { character, style: this.style };
            this.cursorX++;
            if (this.cursorX >= this.columns) {
                this.cursorX = 0;
                this.cursorY++;
                this.ensureLine(this.cursorY);
            }
        }

        lineFeed() {
            this.cursorY++;
            this.cursorX = 0;
            this.ensureLine(this.cursorY);
        }

        saveCursor() {
            this.savedCursor = { x: this.cursorX, y: this.cursorY };
        }

        restoreCursor() {
            this.cursorX = Math.max(0, Math.min(this.columns - 1, this.savedCursor.x));
            this.cursorY = Math.max(0, this.savedCursor.y);
            this.ensureLine(this.cursorY);
        }

        reverseIndex() {
            if (this.cursorY > 0) {
                this.cursorY--;
                return;
            }
            this.lines.unshift([]);
        }

        eraseDisplay(mode) {
            if (mode === 2 || mode === 3) {
                this.lines = [[]];
                this.cursorX = 0;
                this.cursorY = 0;
                return;
            }
            if (mode === 1) {
                for (let row = 0; row < this.cursorY; row++) this.lines[row] = [];
                this.eraseLine(1);
                return;
            }
            this.eraseLine(0);
            this.lines.length = this.cursorY + 1;
        }

        eraseLine(mode) {
            const line = this.ensureLine(this.cursorY);
            if (mode === 2) {
                line.length = 0;
            } else if (mode === 1) {
                for (let column = 0; column <= this.cursorX; column++) delete line[column];
            } else {
                line.length = Math.min(line.length, this.cursorX);
            }
        }

        insertCharacters(count) {
            const line = this.ensureLine(this.cursorY);
            line.splice(this.cursorX, 0, ...Array.from({ length: count }, () => ({ character: ' ', style: this.style })));
            if (line.length > this.columns) line.length = this.columns;
        }

        deleteCharacters(count) {
            const line = this.ensureLine(this.cursorY);
            line.splice(this.cursorX, count);
        }

        eraseCharacters(count) {
            const line = this.ensureLine(this.cursorY);
            for (let column = this.cursorX; column < Math.min(line.length, this.cursorX + count); column++)
                delete line[column];
        }

        insertLines(count) {
            this.lines.splice(this.cursorY, 0, ...Array.from({ length: count }, () => []));
        }

        deleteLines(count) {
            this.lines.splice(this.cursorY, count);
            this.ensureLine(this.cursorY);
        }

        scrollUp(count) {
            for (let index = 0; index < count; index++) this.lines.shift();
            this.ensureLine(this.cursorY);
        }

        scrollDown(count) {
            for (let index = 0; index < count; index++) this.lines.unshift([]);
        }

        ensureLine(row) {
            while (this.lines.length <= row) this.lines.push([]);
            return this.lines[row];
        }

        lineText(line) {
            const end = this.lineEnd(line);
            let text = '';
            for (let column = 0; column < end; column++) text += line[column]?.character || ' ';
            return text;
        }

        lineEnd(line) {
            let end = line.length;
            while (end > 0) {
                const cell = line[end - 1];
                if (cell && cell.character !== ' ')
                    break;
                end--;
            }
            return end;
        }

        trimScrollback() {
            const rowsToRemove = Math.max(0, this.lines.length - this.maxRows);
            if (rowsToRemove > 0) {
                this.lines.splice(0, rowsToRemove);
                this.cursorY = Math.max(0, this.cursorY - rowsToRemove);
                this.savedCursor.y = Math.max(0, this.savedCursor.y - rowsToRemove);
            }

            let textLength = this.toText().length;
            while (textLength > this.maxChars && this.lines.length > 1) {
                textLength -= this.lineText(this.lines[0]).length + 1;
                this.lines.shift();
                this.cursorY = Math.max(0, this.cursorY - 1);
                this.savedCursor.y = Math.max(0, this.savedCursor.y - 1);
            }
        }
    }

    function hasStyle(style) {
        return style.foreground !== null || style.background !== null
            || style.bold || style.dim || style.italic || style.underline
            || style.inverse || style.strike;
    }

    function createStyledElement(text, style) {
        const element = document.createElement('span');
        element.textContent = text;
        const foreground = style.inverse ? style.background || DEFAULT_BACKGROUND : style.foreground;
        const background = style.inverse ? style.foreground || DEFAULT_FOREGROUND : style.background;
        if (foreground) element.style.color = foreground;
        if (background) element.style.backgroundColor = background;
        if (style.bold) element.style.fontWeight = '700';
        if (style.dim) element.style.opacity = '0.72';
        if (style.italic) element.style.fontStyle = 'italic';
        if (style.underline || style.strike) {
            element.style.textDecoration = [
                style.underline ? 'underline' : '',
                style.strike ? 'line-through' : ''
            ].filter(Boolean).join(' ');
        }
        return element;
    }

    class TerminalRenderer {
        constructor(element, options = {}) {
            this.element = element;
            this.buffer = new TerminalBuffer(options);
            this.render();
        }

        reset() {
            this.buffer.reset();
            this.render();
        }

        write(data) {
            this.buffer.write(data);
            this.render();
        }

        render() {
            const fragment = document.createDocumentFragment();
            for (let row = 0; row < this.buffer.lines.length; row++) {
                for (const run of this.buffer.getLineRuns(row)) {
                    if (hasStyle(run.style)) fragment.appendChild(createStyledElement(run.text, run.style));
                    else fragment.appendChild(document.createTextNode(run.text));
                }
                if (row < this.buffer.lines.length - 1) fragment.appendChild(document.createTextNode('\n'));
            }
            this.element.replaceChildren(fragment);
            this.element.scrollTop = this.element.scrollHeight;
        }
    }

    TerminalRenderer.Buffer = TerminalBuffer;
    global.BarkFluffTerminal = TerminalRenderer;
})(window);
