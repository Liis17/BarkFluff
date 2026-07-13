/**
 * UI sound feedback — synthesizes short sounds live via the Web Audio API.
 * No audio files, no dependencies. Ported from https://github.com/Danilaa1/cuelume (MIT).
 * Exposes: BF.sound
 */
(function () {
    'use strict';

    window.BF = window.BF || {};

    var SOURCE_STOP_PADDING = 0.05;
    var CLEANUP_MARGIN = 0.05;
    var INAUDIBLE_GAIN = 0.001;

    var RECIPES = {
        chime: {
            masterGain: 0.5,
            layers: [
                { kind: 'tone', waveform: 'sine', frequency: 1046.5, attack: 0.006, decay: 0.22, peak: 0.09 },
                { kind: 'tone', waveform: 'sine', frequency: 1568, offset: 0.09, attack: 0.006, decay: 0.26, peak: 0.08 }
            ],
            shimmer: { delay: 0.12, feedback: 0.25, wet: 0.18, lowpass: 4000 }
        },
        sparkle: {
            masterGain: 0.5,
            layers: [
                { kind: 'tone', waveform: 'sine', frequency: 1760, offset: 0, attack: 0.003, decay: 0.09, peak: 0.045 },
                { kind: 'tone', waveform: 'sine', frequency: 2217, offset: 0.045, attack: 0.003, decay: 0.09, peak: 0.04 },
                { kind: 'tone', waveform: 'sine', frequency: 2637, offset: 0.09, attack: 0.003, decay: 0.1, peak: 0.038 },
                { kind: 'tone', waveform: 'sine', frequency: 3520, offset: 0.135, attack: 0.003, decay: 0.12, peak: 0.032 }
            ],
            shimmer: { delay: 0.07, feedback: 0.35, wet: 0.22, lowpass: 6000 }
        },
        droplet: {
            masterGain: 0.55,
            layers: [
                { kind: 'tone', waveform: 'sine', frequency: 1200, glideTo: 550, glideTime: 0.14, attack: 0.004, decay: 0.2, peak: 0.075 }
            ],
            shimmer: { delay: 0.09, feedback: 0.2, wet: 0.15, lowpass: 3000 }
        },
        bloom: {
            masterGain: 0.5,
            layers: [
                { kind: 'tone', waveform: 'sine', frequency: 528, attack: 0.06, decay: 0.32, peak: 0.06 },
                { kind: 'tone', waveform: 'sine', frequency: 528, detune: 12, attack: 0.06, decay: 0.34, peak: 0.05 }
            ],
            shimmer: { delay: 0.15, feedback: 0.2, wet: 0.12, lowpass: 2500 }
        },
        whisper: {
            masterGain: 0.5,
            layers: [
                { kind: 'noise', filterType: 'lowpass', filterFrequency: 1200, filterQ: 0.7, attack: 0.04, decay: 0.16, peak: 0.05 }
            ]
        },
        tick: {
            masterGain: 0.4,
            layers: [
                { kind: 'noise', filterType: 'bandpass', filterFrequency: 5400, filterQ: 1.8, attack: 0.001, decay: 0.018, peak: 0.14 },
                { kind: 'tone', waveform: 'sine', frequency: 2600, attack: 0.001, decay: 0.012, peak: 0.018 }
            ]
        },
        press: {
            masterGain: 0.4,
            layers: [
                { kind: 'noise', filterType: 'bandpass', filterFrequency: 1700, filterQ: 1.4, attack: 0.001, decay: 0.02, peak: 0.13 }
            ]
        },
        release: {
            masterGain: 0.4,
            layers: [
                { kind: 'noise', filterType: 'bandpass', filterFrequency: 4600, filterQ: 1.8, attack: 0.001, decay: 0.016, peak: 0.12 },
                { kind: 'tone', waveform: 'sine', frequency: 3200, offset: 0.006, attack: 0.001, decay: 0.05, peak: 0.02 }
            ]
        },
        toggle: {
            masterGain: 0.4,
            layers: [
                { kind: 'noise', filterType: 'bandpass', filterFrequency: 2200, filterQ: 1.6, attack: 0.001, decay: 0.016, peak: 0.12 },
                { kind: 'noise', filterType: 'bandpass', filterFrequency: 3800, filterQ: 1.6, offset: 0.024, attack: 0.001, decay: 0.02, peak: 0.1 }
            ]
        },
        success: {
            masterGain: 0.5,
            layers: [
                { kind: 'tone', waveform: 'sine', frequency: 880, attack: 0.004, decay: 0.09, peak: 0.06 },
                { kind: 'tone', waveform: 'sine', frequency: 1108.73, offset: 0.06, attack: 0.004, decay: 0.1, peak: 0.06 },
                { kind: 'tone', waveform: 'sine', frequency: 1318.51, offset: 0.12, attack: 0.004, decay: 0.18, peak: 0.07 }
            ],
            shimmer: { delay: 0.1, feedback: 0.22, wet: 0.16, lowpass: 4500 }
        }
    };

    function renderTone(context, destination, layer, startTime) {
        var oscillator = context.createOscillator();
        oscillator.type = layer.waveform;
        oscillator.frequency.setValueAtTime(layer.frequency, startTime);
        if (layer.detune) oscillator.detune.value = layer.detune;

        if (layer.glideTo !== undefined) {
            var glideTime = layer.glideTime !== undefined ? layer.glideTime : (layer.attack + layer.decay);
            oscillator.frequency.exponentialRampToValueAtTime(layer.glideTo, startTime + glideTime);
        }

        var gain = context.createGain();
        gain.gain.setValueAtTime(0.0001, startTime);
        gain.gain.exponentialRampToValueAtTime(layer.peak, startTime + layer.attack);
        gain.gain.exponentialRampToValueAtTime(0.0001, startTime + layer.attack + layer.decay);

        oscillator.connect(gain).connect(destination);
        oscillator.start(startTime);
        oscillator.stop(startTime + layer.attack + layer.decay + SOURCE_STOP_PADDING);
    }

    function renderNoise(context, destination, layer, startTime) {
        var duration = layer.attack + layer.decay + SOURCE_STOP_PADDING;
        var length = Math.max(1, Math.floor(duration * context.sampleRate));
        var buffer = context.createBuffer(1, length, context.sampleRate);
        var data = buffer.getChannelData(0);
        for (var i = 0; i < length; i++) data[i] = 2 * Math.random() - 1;

        var source = context.createBufferSource();
        source.buffer = buffer;

        var filter = context.createBiquadFilter();
        filter.type = layer.filterType;
        filter.frequency.value = layer.filterFrequency;
        if (layer.filterQ !== undefined) filter.Q.value = layer.filterQ;

        var gain = context.createGain();
        gain.gain.setValueAtTime(0.0001, startTime);
        gain.gain.exponentialRampToValueAtTime(layer.peak, startTime + layer.attack);
        gain.gain.exponentialRampToValueAtTime(0.0001, startTime + layer.attack + layer.decay);

        source.connect(filter).connect(gain).connect(destination);
        source.start(startTime);
        source.stop(startTime + duration);
    }

    function attachShimmer(context, source, destination, shimmer) {
        var delay = context.createDelay(1);
        delay.delayTime.value = shimmer.delay;

        var feedbackFilter = context.createBiquadFilter();
        feedbackFilter.type = 'lowpass';
        feedbackFilter.frequency.value = shimmer.lowpass;

        var feedbackGain = context.createGain();
        feedbackGain.gain.value = shimmer.feedback;

        var wetGain = context.createGain();
        wetGain.gain.value = shimmer.wet;

        source.connect(delay);
        delay.connect(feedbackFilter);
        feedbackFilter.connect(feedbackGain);
        feedbackGain.connect(delay);
        feedbackFilter.connect(wetGain);
        wetGain.connect(destination);

        return [delay, feedbackFilter, feedbackGain, wetGain];
    }

    function sourceEnd(recipe) {
        var max = 0;
        for (var i = 0; i < recipe.layers.length; i++) {
            var layer = recipe.layers[i];
            var end = (layer.offset || 0) + layer.attack + layer.decay + SOURCE_STOP_PADDING;
            if (end > max) max = end;
        }
        return max;
    }

    function shimmerTail(shimmer) {
        if (!shimmer || shimmer.feedback <= 0) return 0;
        if (shimmer.feedback >= 1) return shimmer.delay;
        return shimmer.delay * (1 + Math.ceil(Math.log(INAUDIBLE_GAIN) / Math.log(shimmer.feedback)));
    }

    function renderRecipe(context, recipe) {
        var now = context.currentTime;
        var master = context.createGain();
        master.gain.value = recipe.masterGain;
        master.connect(context.destination);

        var shimmerNodes = recipe.shimmer ? attachShimmer(context, master, context.destination, recipe.shimmer) : [];

        for (var i = 0; i < recipe.layers.length; i++) {
            var layer = recipe.layers[i];
            var startTime = now + (layer.offset || 0);
            if (layer.kind === 'tone') renderTone(context, master, layer, startTime);
            else renderNoise(context, master, layer, startTime);
        }

        var cleanupAfterMs = (sourceEnd(recipe) + shimmerTail(recipe.shimmer) + CLEANUP_MARGIN) * 1000;
        setTimeout(function () {
            master.disconnect();
            for (var j = 0; j < shimmerNodes.length; j++) shimmerNodes[j].disconnect();
        }, cleanupAfterMs);
    }

    var sharedContext = null;
    var enabled = true;

    function setEnabled(value) {
        if (typeof value === 'boolean') enabled = value;
    }

    function getAudioContext() {
        if (sharedContext) return sharedContext;
        if (typeof window === 'undefined') return null;
        var Ctor = window.AudioContext || window.webkitAudioContext;
        if (!Ctor) return null;
        try {
            sharedContext = new Ctor();
        } catch (e) {
            return null;
        }
        return sharedContext;
    }

    // Async события (входящие сообщения из WebSocket/gRPC) не считаются user gesture —
    // браузер может бесконечно держать AudioContext suspended, если resume() ни разу
    // не вызывался из реального клика/нажатия. Резюмим заранее на любой жест и при
    // возврате фокуса вкладки (Chrome усыпляет контекст после ~30с простоя), чтобы
    // к моменту play() из async-хендлера контекст уже был running.
    function eagerResume() {
        var context = getAudioContext();
        if (context && context.state !== 'running') {
            try { context.resume(); } catch (e) {}
        }
    }

    if (typeof document !== 'undefined') {
        ['pointerdown', 'keydown', 'touchstart'].forEach(function (evt) {
            document.addEventListener(evt, eagerResume, { passive: true });
        });
        document.addEventListener('visibilitychange', function () {
            if (!document.hidden) eagerResume();
        });
    }

    function play(sound) {
        sound = sound || 'chime';
        if (!enabled || !RECIPES.hasOwnProperty(sound)) return;

        var context = getAudioContext();
        if (!context) return;

        var recipe = RECIPES[sound];
        if (context.state === 'running') {
            renderRecipe(context, recipe);
        } else {
            try {
                context.resume().then(function () {
                    if (enabled && context.state === 'running') renderRecipe(context, recipe);
                }, function () {});
            } catch (e) {
                // Some browsers throw synchronously when audio is blocked.
            }
        }
    }

    window.BF.sound = {
        play: play,
        setEnabled: setEnabled
    };
})();
