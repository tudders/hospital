async (page) => {
  const snapshot = await page.evaluate(async () => {
    const { sampleHospital } = await import('/src/lib/hospital.ts');
    return sampleHospital();
  });
  snapshot.source = 'sql';
  const bed = snapshot.beds.find(b => b.id === 'sample-3-4-6-6');
  Object.assign(bed, { patientId: 'locate-check', patientName: 'Synthetic Locate', status: 'occupied' });
  const patient = { id: 'locate-check', mrn: 'TEST-LOCATE', givenName: 'Synthetic', familyName: 'Locate', dateOfBirth: '1990-01-01', registeredAt: snapshot.asOf, version: 1 };
  await page.route('**/api/**', async route => {
    const path = route.request().url().replace(/^https?:\/\/[^/]+/, '').split('?')[0];
    if (path.startsWith('/api/telemetry')) return route.fulfill({ status: 204 });
    const data = path === '/api/auth/me' ? { name: 'UI verification', roles: ['viewer'] }
      : path === '/api/hospital-occupancy' ? snapshot
      : path === '/api/patients' ? [patient]
      : path === '/api/admissions' ? [{ id: 'admission-check', patientId: patient.id, ward: bed.wardName, status: 'Admitted', admittedAt: snapshot.asOf, dischargedAt: null, version: 1 }]
      : [];
    await route.fulfill({ json: data });
  });
  await page.evaluate(() => sessionStorage.setItem('alcidion.token', 'synthetic-ui-check'));
  await page.setViewportSize({ width: 1920, height: 1080 });
  await page.reload();
  await page.getByRole('heading', { name: 'Hospital at a glance.' }).waitFor();
}
