import { test } from 'node:test'
import assert from 'node:assert/strict'
import { hospitalHierarchy, occupancy, sampleHospital } from './hospital.ts'

test('all 720 beds are reachable through five floors, four wards, six rooms and six beds', () => {
  const sample = sampleHospital()
  const [hospital] = hospitalHierarchy(sample.beds.toReversed())
  assert.equal(hospital.floors.length, 5)
  const reachable = []
  for (const floor of hospital.floors) {
    assert.equal(floor.wards.length, 4)
    for (const ward of floor.wards) {
      assert.equal(ward.rooms.length, 6)
      for (const room of ward.rooms) {
        assert.deepEqual(room.beds.map(b => b.number), [1, 2, 3, 4, 5, 6])
        reachable.push(...room.beds.map(b => b.id))
      }
    }
  }
  assert.equal(new Set(reachable).size, 720)
  assert.equal(occupancy(hospital.beds).occupied, 480)
  assert.equal(hospital.floors.reduce((n, f) => n + occupancy(f.beds).occupied, 0), 480)
})

test('empty and inactive inventory does not yield invalid occupancy percentages', () => {
  assert.equal(occupancy([]).percent, 0)
  const beds = sampleHospital().beds.slice(0, 3)
  beds.forEach(b => { b.status = 'inactive' })
  assert.equal(occupancy(beds).percent, 0)
  beds[0].status = 'occupied'
  beds[1].status = 'blocked'
  assert.equal(occupancy(beds).percent, 50)
})

test('multiple hospitals remain separate and historical sample times change beds deterministically', () => {
  const first = sampleHospital()
  const other = first.beds.slice(0, 1).map(b => ({ ...b, id: 'other', hospitalId: 'other', floorId: 'other', wardId: 'other', roomId: 'other' }))
  assert.equal(hospitalHierarchy([...first.beds, ...other]).length, 2)
  const at = '2026-09-12T07:00:00.000Z'
  assert.deepEqual(sampleHospital(at).beds, sampleHospital(at).beds)
  assert.notDeepEqual(sampleHospital(at).beds, first.beds)
  assert.equal(first.source, 'sample')
})
