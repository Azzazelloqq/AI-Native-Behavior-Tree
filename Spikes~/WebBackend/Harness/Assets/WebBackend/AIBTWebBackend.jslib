mergeInto(LibraryManager.library, {
  AIBT_ReportResult: function (jsonPointer) {
    var result = JSON.parse(UTF8ToString(jsonPointer));
    result.browser = {
      userAgent: navigator.userAgent,
      href: window.location.href
    };
    window.AIBT_WEB_RESULT = result;
    fetch('/__aibt_result', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(result)
    }).catch(function (error) {
      console.error('AIBT result delivery failed', error);
    });
  }
});
