export type BedStatus = 'occupied' | 'available' | 'blocked' | 'inactive'
export type HospitalBed = {
  id: string; code: string; number: number
  hospitalId: string; hospitalName: string
  floorId: string; floorName: string; floorNumber: number
  wardId: string; wardName: string
  roomId: string; roomName: string; roomNumber: number
  status: BedStatus; patientId?: string | null; patientName?: string | null
}
export type HospitalSnapshot = {
  asOf: string; capturedAt: string; source: 'sql' | 'sample'; beds: HospitalBed[]
}
export type OccupancyCounts = Record<BedStatus, number> & { total: number; percent: number }
export type HospitalRoom = { id: string; name: string; number: number; beds: HospitalBed[] }
export type HospitalWard = { id: string; name: string; rooms: HospitalRoom[]; beds: HospitalBed[] }
export type HospitalFloor = { id: string; name: string; number: number; wards: HospitalWard[]; beds: HospitalBed[] }
export type Hospital = { id: string; name: string; floors: HospitalFloor[]; beds: HospitalBed[] }

export function occupancy(beds: HospitalBed[]): OccupancyCounts {
  const counts: OccupancyCounts = { occupied: 0, available: 0, blocked: 0, inactive: 0, total: beds.length, percent: 0 }
  for (const bed of beds) counts[bed.status]++
  const active = counts.total - counts.inactive
  counts.percent = active ? Math.round(counts.occupied / active * 100) : 0
  return counts
}

export function hospitalHierarchy(beds: HospitalBed[]): Hospital[] {
  const hospitals = new Map<string, Hospital>()
  const floors = new Map<string, HospitalFloor>()
  const wards = new Map<string, HospitalWard>()
  const rooms = new Map<string, HospitalRoom>()
  for (const bed of beds) {
    let hospital = hospitals.get(bed.hospitalId)
    if (!hospital) {
      hospital = { id: bed.hospitalId, name: bed.hospitalName, floors: [], beds: [] }
      hospitals.set(hospital.id, hospital)
    }
    let floor = floors.get(bed.floorId)
    if (!floor) {
      floor = { id: bed.floorId, name: bed.floorName, number: bed.floorNumber, wards: [], beds: [] }
      floors.set(floor.id, floor); hospital.floors.push(floor)
    }
    let ward = wards.get(bed.wardId)
    if (!ward) {
      ward = { id: bed.wardId, name: bed.wardName, rooms: [], beds: [] }
      wards.set(ward.id, ward); floor.wards.push(ward)
    }
    let room = rooms.get(bed.roomId)
    if (!room) {
      room = { id: bed.roomId, name: bed.roomName, number: bed.roomNumber, beds: [] }
      rooms.set(room.id, room); ward.rooms.push(room)
    }
    hospital.beds.push(bed); floor.beds.push(bed); ward.beds.push(bed); room.beds.push(bed)
  }
  for (const hospital of hospitals.values()) {
    hospital.floors.sort((a, b) => a.number - b.number)
    for (const floor of hospital.floors) {
      floor.wards.sort((a, b) => a.id.localeCompare(b.id))
      for (const ward of floor.wards) {
        ward.rooms.sort((a, b) => a.number - b.number)
        for (const room of ward.rooms) room.beds.sort((a, b) => a.number - b.number)
      }
    }
  }
  return [...hospitals.values()]
}

export const SAMPLE_TIME = '2026-09-12T06:00:00.000Z'
const specialties = ['Emergency', 'Acute medicine', 'Short stay', 'Assessment', 'General medicine', 'Cardiology', 'Respiratory', 'Neurology', 'General surgery', 'Orthopaedics', 'Urology', 'Recovery', 'Intensive care', 'High dependency', 'Oncology', 'Renal', 'Maternity', 'Paediatrics', 'Rehabilitation', 'Older persons care']

/** Explicit synthetic preview; never mixed into a database response. */
export function sampleHospital(at = SAMPLE_TIME): HospitalSnapshot {
  const beds: HospitalBed[] = []
  const hours = Math.floor((Date.parse(at) - Date.parse(SAMPLE_TIME)) / 3_600_000)
  for (let f = 1; f <= 5; f++) for (let w = 1; w <= 4; w++) for (let r = 1; r <= 6; r++) for (let b = 1; b <= 6; b++) {
    const slot = (r - 1) * 6 + b - 1
    const shifted = ((slot + hours) % 36 + 36) % 36
    beds.push({
      id: `sample-${f}-${w}-${r}-${b}`, code: `F${f}-W${w}-R${r}-B${b}`, number: b,
      hospitalId: 'sample-hospital', hospitalName: 'Alcidion General Hospital',
      floorId: `sample-floor-${f}`, floorName: `Level ${f}`, floorNumber: f,
      wardId: `sample-ward-${f}-${w}`, wardName: specialties[(f - 1) * 4 + w - 1],
      roomId: `sample-room-${f}-${w}-${r}`, roomName: `Room ${r}`, roomNumber: r,
      status: shifted < 24 ? 'occupied' : shifted === 34 ? 'blocked' : shifted === 35 && w === 4 ? 'inactive' : 'available',
    })
  }
  return { asOf: at, capturedAt: new Date().toISOString(), source: 'sample', beds }
}

export function localDateTime(date: Date): string {
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
}
