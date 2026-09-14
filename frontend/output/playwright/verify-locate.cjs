async (page) => {
  const check = (condition, message) => { if (!condition) throw new Error(message); };
  const hospitalX = (await page.getByRole('heading', { name: 'Hospital at a glance.' }).boundingBox()).x;
  const center = await page.locator('.model-bed.focused .model-top').evaluate(el => {
    const box = el.getBBox();
    return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
  });
  check(Math.abs(center.x - 400) < 0.1 && Math.abs(center.y - 380) < 0.1, 'Bed is not centered');
  check(await page.getByRole('checkbox', { name: 'Auto-refresh' }).count() === 0, 'Auto-refresh still visible');
  await page.getByRole('button', { name: 'Level 1', exact: true }).click();
  const refreshed = page.waitForResponse('**/api/hospital-occupancy');
  await page.getByRole('button', { name: 'Refresh' }).click();
  await refreshed;
  await page.getByText('Snapshot · refresh manually', { exact: true }).waitFor();
  await page.clock.install();
  let backgroundRequests = 0;
  page.on('request', req => { if (req.url().includes('/api/hospital-occupancy')) backgroundRequests++; });
  await page.clock.runFor(31_000);
  check(backgroundRequests === 0, 'Unexpected automatic refresh');
  check(await page.getByRole('button', { name: 'Level 1', exact: true }).getAttribute('aria-pressed') === 'true', 'Refresh repeated locate');
  check(await page.locator('.model-bed.focused').count() === 0, 'Refresh restored bed focus');
  await page.getByRole('button', { name: 'Patient flow', exact: true }).click();
  const flowX = (await page.getByRole('heading', { name: 'Patient flow.', exact: true }).boundingBox()).x;
  check(flowX === hospitalX, 'Page headings are not left aligned');
  await page.screenshot({ path: 'output/playwright/patient-flow.png' });
  await page.getByRole('button', { name: 'Hospital overview' }).click();
  await page.getByRole('button', { name: 'All floors', exact: true }).waitFor();
  check(await page.getByRole('button', { name: 'All floors', exact: true }).getAttribute('aria-pressed') === 'true', 'Navigation repeated locate');
  await page.getByRole('button', { name: 'Patient flow', exact: true }).click();
  await page.getByRole('searchbox', { name: 'Search by MRN or patient name' }).fill('Synthetic');
  await page.clock.runFor(300);
  await page.getByRole('button', { name: 'SL Synthetic Locate TEST-' }).click();
  await page.getByRole('button', { name: 'Locate in hospital' }).click();
  await page.clock.runFor(100);
  await page.locator('.model-bed.focused').waitFor();
  check(await page.getByRole('button', { name: 'Level 3', exact: true }).getAttribute('aria-pressed') === 'true', 'Repeat explicit locate failed');
  console.log({ centered: center, headingLeft: { hospital: hospitalX, patientFlow: flowX }, refreshPreservesFloor: true, noPolling: true, navigationDoesNotRelocate: true, explicitRelocateWorks: true });
}
