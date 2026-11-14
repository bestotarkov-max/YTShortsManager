// Мост между страницей YouTube и локальным приложением YT Shorts Manager.
// Приложение слушает 127.0.0.1:5000 (резервный адрес — localhost:5000).

const SERVERS = ["http://127.0.0.1:5000", "http://localhost:5000"];

function buildCookiesTxt(cookies) {
  const lines = ["# Netscape HTTP Cookie File"];

  for (const c of cookies) {
    if (!c || !c.domain) continue;

    let domain = c.domain;
    if (!domain.startsWith(".")) domain = "." + domain;

    const includeSub = domain.startsWith(".") ? "TRUE" : "FALSE";
    const secure = c.secure ? "TRUE" : "FALSE";
    const expiry = c.expirationDate ? Math.floor(c.expirationDate) : 0;
    const path = c.path || "/";

    // httpOnly-куки помечаются префиксом #HttpOnly_ (стандарт Netscape).
    const prefix = c.httpOnly ? "#HttpOnly_" : "";

    lines.push(`${prefix}${domain}\t${includeSub}\t${path}\t${secure}\t${expiry}\t${c.name}\t${c.value}`);
  }

  return lines.join("\n") + "\n";
}

function getYoutubeCookies() {
  return new Promise((resolve) => {
    chrome.cookies.getAll({ domain: "youtube.com" }, (cookies) => {
      if (chrome.runtime.lastError || !cookies) {
        resolve(null);
        return;
      }
      resolve(buildCookiesTxt(cookies));
    });
  });
}

/**
 * POST на локальный сервер с автоматическим перебором адресов.
 * Первый успешный ответ возвращается, иначе пробрасывается последняя ошибка.
 */
async function postJson(path, payload, timeoutMs) {
  let lastError = null;

  for (const base of SERVERS) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);

    try {
      const response = await fetch(base + path, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
        signal: controller.signal
      });

      if (!response.ok) {
        if (response.status === 429) {
          throw new Error("Очередь загрузок переполнена. Дождитесь окончания текущих загрузок.");
        }
        throw new Error(`Сервер вернул ошибку ${response.status}`);
      }
      return await response.json();
    } catch (error) {
      lastError = error;
    } finally {
      clearTimeout(timer);
    }
  }

  throw lastError || new Error("Приложение не запущено");
}

chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
  if (request.type === "sendToPython") {
    (async () => {
      try {
        const cookiesTxt = await getYoutubeCookies();
        if (!cookiesTxt) {
          sendResponse({ success: false, error: "Не удалось получить куки YouTube." });
          return;
        }

        const data = await postJson("/download", { ...request.data, cookies_data: cookiesTxt }, 60000);
        sendResponse({ success: true, data });
      } catch (error) {
        sendResponse({ success: false, error: error.message });
      }
    })();

    return true; // асинхронный ответ
  }

  if (request.type === "getVideoInfo") {
    (async () => {
      try {
        if (!request.url || !/^https?:\/\//i.test(request.url)) {
          sendResponse({ status: "error", message: "Некорректная ссылка." });
          return;
        }

        const cookiesTxt = (await getYoutubeCookies()) || "";
        const data = await postJson("/get_info", { url: request.url, cookies_data: cookiesTxt }, 120000);
        sendResponse(data);
      } catch (error) {
        sendResponse({ status: "error", message: error.message });
      }
    })();

    return true; // асинхронный ответ
  }

  return false;
});
