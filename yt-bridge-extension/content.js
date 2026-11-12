console.log("YT Shorts Downloader: Новый понятный интерфейс!");

let isScrolling = false;
let scrollTimeout;

const LANG = {
    RU: { 
        title: "Скачать видео", duration: "Длительность:", unknown: "Неизвестна", loading: "Загрузка...", 
        quality: "Качество:", best: "Лучшее", audio: "Только звук (MP3)", 
        extras: "Дополнительно:", subs: "Субтитры", thumb: "Обложка", sponsor: "Вырезать рекламу", playlist: "Скачать весь плейлист",
        full: "Скачать видео", trim_sec: "Вырезать фрагмент (в секундах):", trim_min: "Вырезать фрагмент (в минутах):", 
        from: "От", to: "До", trim_btn: "Вырезать и скачать" 
    },
    EN: { 
        title: "Download Video", duration: "Duration:", unknown: "Unknown", loading: "Loading...", 
        quality: "Quality:", best: "Best", audio: "Audio only (MP3)", 
        extras: "Extras:", subs: "Subtitles", thumb: "Thumbnail", sponsor: "Remove sponsor ads", playlist: "Download full playlist",
        full: "Download Video", trim_sec: "Trim fragment (in seconds):", trim_min: "Trim fragment (in minutes):", 
        from: "From", to: "To", trim_btn: "Trim & Download" 
    }
};
let currentLang = "RU";

function isShortsPage() {
    return window.location.href.includes('/shorts/');
}

function createUI() {
  if (document.getElementById('yt-dl-main-btn')) return;

  const mainBtn = document.createElement('button');
  mainBtn.id = 'yt-dl-main-btn';
  mainBtn.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" height="24" viewBox="0 0 24 24" width="24" fill="white"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></svg>`;
  
  mainBtn.style.cssText = `position: fixed !important; z-index: 2147483647 !important; background-color: rgba(255, 255, 255, 0.1) !important; color: white !important; border: none !important; border-radius: 50% !important; width: 48px !important; height: 48px !important; cursor: pointer !important; display: none; align-items: center !important; justify-content: center !important; transition: background-color 0.2s, opacity 0.2s !important; font-family: 'Segoe UI', Roboto, sans-serif !important;`;

  const modal = document.createElement('div');
  modal.id = 'yt-dl-modal';
  modal.style.cssText = `position: fixed !important; z-index: 2147483647 !important; background-color: #212121 !important; border: 1px solid #333 !important; border-radius: 16px !important; padding: 20px !important; width: 320px !important; color: white !important; font-family: 'Segoe UI', Roboto, Helvetica, Arial, sans-serif !important; display: none; box-shadow: 0 10px 40px rgba(0,0,0,0.8) !important; transition: transform 0.1s ease-out !important;`;

  modal.innerHTML = `
    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 15px;">
        <h3 id="lbl-title" style="margin: 0; font-size: 18px; font-weight: 700; color: #fff;">Скачать видео</h3>
        <select id="select-lang" style="background: #333; color: #aaa; border: 1px solid #444; border-radius: 6px; padding: 2px 5px; font-size: 12px; outline: none; cursor: pointer;"><option value="RU">RU</option><option value="EN">EN</option></select>
    </div>
    
    <div id="video-info" style="text-align: center; font-size: 13px; color: #aaa; margin-bottom: 20px; padding: 8px; background: #1a1a1a; border-radius: 8px;">Загрузка...</div>

    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; padding: 0 5px;">
        <span id="lbl-quality" style="font-size: 14px; color: #ccc;">Качество:</span>
        <select id="opt-quality" style="background: #333; color: white; border: 1px solid #444; border-radius: 6px; padding: 4px 8px; outline: none; cursor: pointer;">
            <option value="best" id="opt-best">Лучшее (до 4K)</option>
            <option value="1080">1080p (Full HD)</option>
            <option value="720">720p (HD)</option>
            <option value="480">480p</option>
        </select>
    </div>

    <label style="display: flex; align-items: center; gap: 10px; margin-bottom: 15px; padding: 10px; background: #2a2a2a; border-radius: 8px; cursor: pointer; border: 1px solid #333; transition: background 0.2s;">
        <input type="checkbox" id="opt-audio" style="width: 16px; height: 16px; accent-color: #cc0000; cursor: pointer;"> 
        <span id="lbl-audio" style="font-size: 14px; font-weight: 600; color: #ff9800;">🎵 Только звук (MP3)</span>
    </label>

    <div style="background: #1a1a1a; border-radius: 8px; padding: 12px; margin-bottom: 20px;">
        <div id="lbl-extras" style="font-size: 12px; font-weight: bold; color: #888; margin-bottom: 10px; text-transform: uppercase; letter-spacing: 0.5px;">Дополнительно:</div>
        
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 8px; font-size: 13px;">
            <label style="display: flex; align-items: center; gap: 8px; cursor: pointer;">
                <input type="checkbox" id="opt-subs" style="accent-color: #cc0000;"> <span id="lbl-subs">Субтитры</span>
            </label>
            <label style="display: flex; align-items: center; gap: 8px; cursor: pointer;">
                <input type="checkbox" id="opt-thumb" style="accent-color: #cc0000;"> <span id="lbl-thumb">Обложка</span>
            </label>
            <label style="display: flex; align-items: center; gap: 8px; cursor: pointer; color: #ffaa00;">
                <input type="checkbox" id="opt-sponsor" style="accent-color: #cc0000;"> <span id="lbl-sponsor">Без рекламы</span>
            </label>
            <label style="display: flex; align-items: center; gap: 8px; cursor: pointer; color: #00aaff;">
                <input type="checkbox" id="opt-playlist" style="accent-color: #cc0000;"> <span id="lbl-playlist">Плейлист</span>
            </label>
        </div>
    </div>

    <button id="btn-full" style="width: 100%; padding: 12px; background: #cc0000; color: #ffffff; border: none; border-radius: 8px; cursor: pointer; font-size: 15px; font-weight: bold; transition: background 0.2s; display: flex; justify-content: center; align-items: center; gap: 8px;">
        ⬇️ <span id="lbl-full">Скачать видео</span>
    </button>

    <div style="margin-top: 20px; border-top: 1px solid #333; padding-top: 15px;">
      <p id="trim-label" style="font-size: 13px; margin: 0 0 10px 0; color: #aaa; font-weight: 600;">Вырезать фрагмент:</p>
      <div style="display: flex; gap: 10px; margin-bottom: 10px;">
        <input type="number" step="any" id="input-start" placeholder="От" value="0" style="width: 50%; padding: 8px; background: #1a1a1a; border: 1px solid #444; color: #fff; border-radius: 6px; font-size: 14px; outline: none; text-align: center;">
        <input type="number" step="any" id="input-end" placeholder="До" style="width: 50%; padding: 8px; background: #1a1a1a; border: 1px solid #444; color: #fff; border-radius: 6px; font-size: 14px; outline: none; text-align: center;">
      </div>
      <button id="btn-trim" style="width: 100%; padding: 10px; background: #333; color: #fff; border: 1px solid #555; border-radius: 8px; cursor: pointer; font-size: 14px; font-weight: bold; transition: background 0.2s;">✂️ <span id="lbl-trim-btn">Вырезать и скачать</span></button>
    </div>
    
    <div style="text-align: right; font-size: 11px; color: #555; margin-top: 15px; font-weight: bold;">IZYXxx</div>
  `;

  document.body.appendChild(mainBtn);
  document.body.appendChild(modal);

  const updateLanguage = (lang) => {
      currentLang = lang;
      document.getElementById('lbl-title').innerText = LANG[lang].title;
      document.getElementById('lbl-quality').innerText = LANG[lang].quality;
      document.getElementById('opt-best').innerText = LANG[lang].best;
      document.getElementById('lbl-audio').innerText = "🎵 " + LANG[lang].audio;
      document.getElementById('lbl-extras').innerText = LANG[lang].extras;
      document.getElementById('lbl-subs').innerText = LANG[lang].subs;
      document.getElementById('lbl-thumb').innerText = LANG[lang].thumb;
      document.getElementById('lbl-sponsor').innerText = LANG[lang].sponsor;
      document.getElementById('lbl-playlist').innerText = LANG[lang].playlist;
      document.getElementById('lbl-full').innerText = LANG[lang].full;
      document.getElementById('lbl-trim-btn').innerText = LANG[lang].trim_btn;
      document.getElementById('input-start').placeholder = LANG[lang].from;
      document.getElementById('input-end').placeholder = LANG[lang].to;
      
      document.getElementById('trim-label').innerText = isShortsPage() ? LANG[lang].trim_sec : LANG[lang].trim_min;
      if (document.getElementById('video-info').innerText !== LANG[lang].loading) { document.getElementById('video-info').innerText = LANG[lang].loading; fetchVideoInfo(); }
  };

  document.getElementById('select-lang').onchange = (e) => updateLanguage(e.target.value);
  
  const btnFull = document.getElementById('btn-full');
  btnFull.onmouseenter = () => btnFull.style.background = '#ff0000';
  btnFull.onmouseleave = () => btnFull.style.background = '#cc0000';
  
  const btnTrim = document.getElementById('btn-trim');
  btnTrim.onmouseenter = () => btnTrim.style.background = '#444';
  btnTrim.onmouseleave = () => btnTrim.style.background = '#333';

  mainBtn.onmouseenter = () => mainBtn.style.backgroundColor = 'rgba(255, 255, 255, 0.2)';
  mainBtn.onmouseleave = () => mainBtn.style.backgroundColor = 'rgba(255, 255, 255, 0.1)';

  mainBtn.onclick = () => {
    if (modal.style.display === 'none' || modal.style.display === '') {
      modal.style.display = 'block';
      document.getElementById('trim-label').innerText = isShortsPage() ? LANG[currentLang].trim_sec : LANG[currentLang].trim_min;
      fetchVideoInfo();
    } else { modal.style.display = 'none'; }
  };

  const fetchVideoInfo = () => {
    const infoDiv = document.getElementById('video-info');
    infoDiv.innerText = LANG[currentLang].loading;
    try {
      chrome.runtime.sendMessage({ type: "getVideoInfo", url: window.location.href }, (res) => {
        if (res && res.status === "success" && res.duration) {
          const mins = Math.floor(res.duration / 60);
          const secs = (res.duration % 60).toString().padStart(2, '0');
          infoDiv.innerHTML = `<span style="color: #00ff00; font-weight: bold;">${mins}:${secs}</span> (${res.duration} сек.)`;
        } else {
          infoDiv.innerText = `${LANG[currentLang].duration} ${LANG[currentLang].unknown}`;
          infoDiv.style.color = "#ff5555"; 
        }
      });
    } catch (err) { infoDiv.innerText = "Error"; }
  };

  const sendRequest = (action, start = 0, end = 0) => {
    modal.style.display = 'none'; mainBtn.innerHTML = '⏳';
    const payload = { 
        action, url: window.location.href, start, end, 
        quality: document.getElementById('opt-quality').value, 
        audio_only: document.getElementById('opt-audio').checked, 
        subs: document.getElementById('opt-subs').checked, 
        thumb: document.getElementById('opt-thumb').checked,
        sponsor: document.getElementById('opt-sponsor').checked,
        playlist: document.getElementById('opt-playlist').checked
    };
    try {
      chrome.runtime.sendMessage({ type: "sendToPython", data: payload }, (res) => {
        if (chrome.runtime.lastError || !res || !res.success) { mainBtn.innerHTML = '❌'; alert('Ошибка: Python не запущен!'); } else { mainBtn.innerHTML = '✅'; }
        setTimeout(() => mainBtn.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" height="24" viewBox="0 0 24 24" width="24" fill="white"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></svg>`, 2500);
      });
    } catch (err) { mainBtn.innerHTML = '❌'; setTimeout(() => mainBtn.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" height="24" viewBox="0 0 24 24" width="24" fill="white"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></svg>`, 2500); alert('Обновите F5!'); }
  };

  document.getElementById('btn-full').onclick = () => sendRequest("full");
  document.getElementById('btn-trim').onclick = () => {
    const start_sec = Math.round((parseFloat(document.getElementById('input-start').value) || 0) * (isShortsPage() ? 1 : 60));
    const end_sec = Math.round((parseFloat(document.getElementById('input-end').value) || 0) * (isShortsPage() ? 1 : 60));
    if (end_sec <= start_sec && end_sec !== 0) return alert("Ошибка: Время конца должно быть больше времени начала");
    sendRequest("trim", start_sec, end_sec);
  };
}

function positionModal(modal, btnTop, btnHeight) {
    const modalHeightEstimate = 460; 
    if (window.innerHeight - (btnTop + btnHeight) > modalHeightEstimate) {
        modal.style.top = `${Math.round(btnTop + btnHeight + 10)}px`; modal.style.transform = 'none';
    } else {
        modal.style.top = `${Math.round(btnTop - 10)}px`; modal.style.transform = 'translateY(-100%)';
    }
}

function updateUI() {
  const mainBtn = document.getElementById('yt-dl-main-btn');
  const modal = document.getElementById('yt-dl-modal');
  if (!mainBtn || !modal) return;

  const isShorts = isShortsPage();
  const isWatch = window.location.href.includes('/watch');
  
  if (!isShorts && !isWatch) {
    mainBtn.style.display = 'none'; modal.style.display = 'none'; return; 
  }

  if (isScrolling) { mainBtn.style.opacity = '0'; modal.style.display = 'none'; return; }

  const isFullscreen = document.documentElement.fullscreenElement || document.fullscreenElement || document.querySelector('.ytp-fullscreen');
  
  if (isFullscreen) {
    mainBtn.style.display = 'flex'; mainBtn.style.opacity = '0';
    mainBtn.style.top = '50px'; mainBtn.style.right = '50px'; mainBtn.style.left = 'auto';
    mainBtn.onmouseenter = () => { mainBtn.style.opacity = '1'; }; mainBtn.onmouseleave = () => { mainBtn.style.opacity = '0'; };
    if (modal) modal.style.display = 'none'; return;
  }

  mainBtn.style.display = 'flex'; 
  mainBtn.style.opacity = '1';
  mainBtn.style.right = 'auto'; // Сброс right при выходе из фуллскрина

  if (isShorts) {
    const likeBtn = document.querySelector('ytd-reel-player-overlay-renderer #like-button') || 
                    document.querySelector('ytd-reel-player-overlay-renderer [aria-label="Нравится"]') ||
                    document.querySelector('ytd-reel-player-overlay-renderer [aria-label="Like"]');
    
    if (likeBtn) {
      const rect = likeBtn.getBoundingClientRect();
      if (rect.width > 0 && rect.top > 0) {
        const centerX = rect.left + (rect.width / 2);
        const btnTop = rect.top - 65;
        mainBtn.style.left = `${Math.round(centerX - 24)}px`; mainBtn.style.top = `${btnTop}px`;
        modal.style.left = `${Math.max(10, Math.round(centerX - 340))}px`; positionModal(modal, btnTop, 48);
        return;
      }
    }
    const video = document.querySelector('video');
    if (video) {
      const vRect = video.getBoundingClientRect();
      if (vRect.width > 0) {
        const btnTop = vRect.top + vRect.height / 2 - 24;
        mainBtn.style.left = `${Math.round(vRect.right + 15)}px`; mainBtn.style.top = `${btnTop}px`;
        modal.style.left = `${Math.round(vRect.right + 75)}px`; positionModal(modal, btnTop, 48);
      }
    }
  } else if (isWatch) {
    const actionMenu = document.querySelector('#top-level-buttons-computed');
    if (actionMenu) {
      const rect = actionMenu.getBoundingClientRect();
      if (rect.top >= 0 && rect.bottom <= window.innerHeight && rect.width > 0) {
        const btnTop = rect.top + (rect.height / 2) - 24;
        mainBtn.style.left = `${Math.round(rect.left - 60)}px`; mainBtn.style.top = `${btnTop}px`;
        modal.style.left = `${Math.round(rect.left - 340)}px`; positionModal(modal, btnTop, 48);
      }
    }
  }
}

function handleScroll() {
  isScrolling = true;
  if (document.getElementById('yt-dl-main-btn')) document.getElementById('yt-dl-main-btn').style.opacity = '0';
  if (document.getElementById('yt-dl-modal')) document.getElementById('yt-dl-modal').style.display = 'none'; 
  clearTimeout(scrollTimeout);
  scrollTimeout = setTimeout(() => { isScrolling = false; updateUI(); }, 200); 
}

// --- УМНОЕ ОБНОВЛЕНИЕ БЕЗ НАГРУЗКИ НА ЦП ---

window.addEventListener('yt-navigate-finish', () => {
    setTimeout(() => {
        createUI();
        updateUI();
    }, 500);
});

const observer = new MutationObserver((mutations) => {
    if (document.getElementById('yt-dl-main-btn')) {
        updateUI();
    }
});

observer.observe(document.body, { childList: true, subtree: true });

setTimeout(() => {
    createUI();
    updateUI();
}, 1500);

window.addEventListener('wheel', handleScroll, { passive: true });
window.addEventListener('touchmove', handleScroll, { passive: true });
window.addEventListener('scroll', handleScroll, { capture: true, passive: true });
document.addEventListener('fullscreenchange', updateUI);
document.addEventListener('webkitfullscreenchange', updateUI);