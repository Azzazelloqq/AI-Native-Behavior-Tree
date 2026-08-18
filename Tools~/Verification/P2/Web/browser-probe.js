const fs = require('fs');
const { Builder } = require('selenium-webdriver');
const chrome = require('selenium-webdriver/chrome');
const firefox = require('selenium-webdriver/firefox');

async function main() {
  const [browser, url, output] = process.argv.slice(2);
  let builder = new Builder().forBrowser(browser);
  if (browser === 'chrome') {
    const options = new chrome.Options();
    options.addArguments('--headless=new', '--no-sandbox', '--disable-dev-shm-usage',
      '--enable-webgl', '--ignore-gpu-blocklist', '--use-gl=angle', '--use-angle=swiftshader');
    builder = builder.setChromeOptions(options);
  } else if (browser === 'firefox') {
    const options = new firefox.Options();
    options.addArguments('-headless');
    options.setPreference('webgl.force-enabled', true);
    options.setPreference('webgl.disabled', false);
    builder = builder.setFirefoxOptions(options);
  } else throw new Error('Unsupported browser ' + browser);

  const driver = await builder.build();
  try {
    await driver.get(url);
    const deadline = Date.now() + 180000;
    let raw = null;
    while (Date.now() < deadline) {
      raw = await driver.executeScript('return window.AIBT_P2_RESULT || null;');
      if (raw) break;
      await new Promise(resolve => setTimeout(resolve, 500));
    }
    if (!raw) throw new Error('Timed out waiting for AIBT_P2_RESULT.');
    const result = JSON.parse(raw);
    const capabilities = await driver.getCapabilities();
    fs.writeFileSync(output, JSON.stringify({
      browser,
      browserVersion: capabilities.get('browserVersion'),
      platform: capabilities.get('platformName'),
      result
    }, null, 2));
    if (!result.passed || !result.il2cpp || !result.burstEnabled || !result.catalogUsable ||
        result.managedPathSentinel !== 0 || result.executionCode !== 'Success' ||
        result.callbackFailure !== 'Success' || result.callbackStatus !== 'Success' ||
        result.memoryValue !== 38 || !result.zeroAssetIdSentinelPreserved ||
        !result.immediateBudgetedEquivalent || result.lifecycleSteps !== result.budgetSegments ||
        result.measurementIterationsPerSample !== 1024 || !Array.isArray(result.rawNanosecondsPerDispatch) ||
        result.rawNanosecondsPerDispatch.length !== 7 || result.rawNanosecondsPerDispatch.some(x => !(x > 0)) ||
        result.controlledAllocationBytes !== 1048576) process.exitCode = 2;
  } finally {
    await driver.quit();
  }
}

main().catch(error => { console.error(error.stack || error); process.exit(1); });
