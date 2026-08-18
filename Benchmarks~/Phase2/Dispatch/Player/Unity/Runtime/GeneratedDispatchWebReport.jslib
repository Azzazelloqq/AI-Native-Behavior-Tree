mergeInto(LibraryManager.library, {
  AIBTWebReport: function (jsonPtr) {
    var json = UTF8ToString(jsonPtr);
    window.AIBT_P2_RESULT = json;
    document.documentElement.setAttribute('data-aibt-p2-result', json);
  }
});
