chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
  if (request.type === "sendToPython") {
    chrome.cookies.getAll({ domain: "youtube.com" }, (cookies) => {
      if (chrome.runtime.lastError || !cookies) {
          sendResponse({ success: false, error: "Не удалось получить куки YouTube." });
          return;
      }

      let cookiesTxt = "# Netscape HTTP Cookie File\n";
      cookies.forEach(c => {
        let domain = c.domain;
        if (!domain.startsWith(".")) domain = "." + domain;
        const includeSub = domain.startsWith(".") ? "TRUE" : "FALSE";
        const secure = c.secure ? "TRUE" : "FALSE";
        const expiry = c.expirationDate ? Math.floor(c.expirationDate) : 0;
        cookiesTxt += `${domain}\t${includeSub}\t${c.path}\t${secure}\t${expiry}\t${c.name}\t${c.value}\n`;
      });

      fetch('http://127.0.0.1:5000/download', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ 
            ...request.data, 
            cookies_data: cookiesTxt 
        })
      })
      .then(response => {
          if (!response.ok) throw new Error("Сервер вернул ошибку " + response.status);
          return response.json();
      })
      .then(data => sendResponse({ success: true, data: data }))
      .catch(error => sendResponse({ success: false, error: error.message }));
    });
    return true;
  }

  if (request.type === "getVideoInfo") {
    chrome.cookies.getAll({ domain: "youtube.com" }, (cookies) => {
      let cookiesTxt = "# Netscape HTTP Cookie File\n";
      (cookies || []).forEach(c => {
        let domain = c.domain;
        if (!domain.startsWith(".")) domain = "." + domain;
        const includeSub = domain.startsWith(".") ? "TRUE" : "FALSE";
        const secure = c.secure ? "TRUE" : "FALSE";
        const expiry = c.expirationDate ? Math.floor(c.expirationDate) : 0;
        cookiesTxt += `${domain}\t${includeSub}\t${c.path}\t${secure}\t${expiry}\t${c.name}\t${c.value}\n`;
      });

      fetch('http://127.0.0.1:5000/get_info', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ url: request.url, cookies_data: cookiesTxt })
      })
      .then(response => response.json())
      .then(data => sendResponse(data))
      .catch(error => sendResponse({ status: "error", message: error.message }));
    });
    return true;
  }
});