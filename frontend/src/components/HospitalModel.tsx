import type { PointerEvent } from 'react'
import { occupancy, type Hospital, type HospitalBed } from '../lib/hospital'

export type Inspection = { label: string; path: string; beds: HospitalBed[] }
type Props = {
  hospital: Hospital; floorId: string; angle: number; exploded: boolean
  focusedBedId?: string | null
  onInspect: (inspection: Inspection | null) => void
  onSelect: (bed: HospitalBed) => void
}

/** Orthographic projection of real x/y/z coordinates. SVG keeps the scene crisp without a 3D runtime. */
export function HospitalModel({ hospital, floorId, angle, exploded, focusedBedId, onInspect, onSelect }: Props) {
  const single = floorId !== 'all'
  const floors = single ? hospital.floors.filter(f => f.id === floorId) : hospital.floors
  const radians = angle * Math.PI / 180
  const scale = focusedBedId ? 2.15 : single ? 1.55 : 1.08
  const spacing = exploded ? 108 : 46
  const base = single ? 355 : 405 + (floors.length - 1) * spacing / 2
  const focusedPosition = (() => {
    if (!focusedBedId) return null
    for (const [fi, floor] of floors.entries()) for (const [wi, ward] of floor.wards.entries()) {
      const wx = 12 + wi % 2 * 204, wy = 12 + Math.floor(wi / 2) * 132
      for (const [ri, room] of ward.rooms.entries()) for (const [bi, bed] of room.beds.entries()) {
        // Match the rendered room row and target the centre of the bed's top face.
        if (bed.id === focusedBedId) return {
          x: wx + ri % 3 * 62 + 4 + bi % 3 * 18 + 13 / 2,
          y: wy + Math.floor(ri / 3) * 55 + 4 + Math.floor(bi / 3) * 24 + 18 / 2,
          z: (single ? 0 : fi * spacing) + 7,
        }
      }
    }
    return null
  })()
  const projectAt = (x: number, y: number, z: number, multiplier: number) => {
    const px = x - 206, py = y - 132
    return { x: 400 + (px * Math.cos(radians) - py * Math.sin(radians)) * multiplier, y: base + (px * Math.sin(radians) + py * Math.cos(radians)) * .48 * multiplier - z }
  }
  const project = (x: number, y: number, z: number) => {
    const point = projectAt(x, y, z, scale)
    if (!focusedPosition) return `${point.x.toFixed(1)},${point.y.toFixed(1)}`
    const anchor = projectAt(focusedPosition.x, focusedPosition.y, focusedPosition.z, scale)
    return `${(400 + point.x - anchor.x).toFixed(1)},${(380 + point.y - anchor.y).toFixed(1)}`
  }
  const face = (x: number, y: number, z: number, w: number, d: number) =>
    [project(x, y, z), project(x + w, y, z), project(x + w, y + d, z), project(x, y + d, z)].join(' ')
  const box = (x: number, y: number, z: number, w: number, d: number, h: number, className: string) => <g className={className}>
    <polygon data-track="inspect-model-part" className="model-side" points={[project(x, y + d, z), project(x + w, y + d, z), project(x + w, y + d, z - h), project(x, y + d, z - h)].join(' ')} />
    <polygon data-track="inspect-model-part" className="model-side alternate" points={[project(x + w, y, z), project(x + w, y + d, z), project(x + w, y + d, z - h), project(x + w, y, z - h)].join(' ')} />
    <polygon data-track="inspect-model-part" className="model-top" points={face(x, y, z, w, d)} />
  </g>
  function inspect(event: PointerEvent<SVGGElement>, label: string, path: string, beds: HospitalBed[]) {
    event.stopPropagation()
    onInspect({ label, path, beds })
  }

  return <svg className="hospital-model" viewBox="0 0 800 760" role="group" aria-label="Three dimensional hospital occupancy model. Use the adjacent floor and room controls for keyboard navigation."
    data-track="hospital-model" onPointerLeave={() => onInspect(null)}>
    <defs>
      <pattern id="hospital-ground-grid" width="32" height="16" patternUnits="userSpaceOnUse" patternTransform="rotate(-12)">
        <path d="M 32 0 L 0 0 0 16" className="model-gridline" fill="none" />
      </pattern>
    </defs>
    <ellipse cx="400" cy={single ? 465 : 697} rx="300" ry="45" className="model-shadow" />
    <rect x="65" y={single ? 375 : 617} width="670" height="125" fill="url(#hospital-ground-grid)" />
    {floors.map((floor, fi) => {
      const z = single ? 0 : fi * spacing
      return <g key={floor.id} onPointerOver={e => inspect(e, floor.name, hospital.name, floor.beds)} data-track="inspect-floor">
        {box(0, 0, z, 412, 264, 10, 'model-slab')}
        <text x="55" y={base - z + 12} className="model-floor-label">{String(floor.number).padStart(2, '0')}</text>
        <text x="55" y={base - z + 29} className="model-floor-count">{occupancy(floor.beds).occupied}/{floor.beds.length}</text>
        {floor.wards.map((ward, wi) => {
          const wx = 12 + wi % 2 * 204, wy = 12 + Math.floor(wi / 2) * 132
          return <g key={ward.id} className="model-ward" data-track="inspect-ward" onPointerOver={e => inspect(e, ward.name, `${hospital.name} / ${floor.name}`, ward.beds)}>
            <polygon className="model-ward-outline" points={face(wx - 3, wy - 3, z + 1, 190, 114)} />
            {ward.rooms.map((room, ri) => {
              const rx = wx + ri % 3 * 62, ry = wy + Math.floor(ri / 3) * 55
              return <g key={room.id} className="model-room" data-track="inspect-room" onPointerOver={e => inspect(e, room.name, `${floor.name} / ${ward.name}`, room.beds)}>
                {box(rx, ry, z + 3, 58, 51, 3, 'model-room-shell')}
                {room.beds.map((bed, bi) => {
                  const bx = rx + 4 + bi % 3 * 18, by = ry + 4 + Math.floor(bi / 3) * 24
                  return <g key={bed.id} className={`model-bed ${bed.status} ${bed.id === focusedBedId ? 'focused' : ''}`} data-track="inspect-bed"
                    onPointerOver={e => inspect(e, `Bed ${bed.number} · ${bed.status}`, `${floor.name} / ${ward.name} / ${room.name}`, [bed])}
                    onClick={e => { e.stopPropagation(); onSelect(bed) }}>
                    <title>{`${floor.name}, ${ward.name}, ${room.name}, Bed ${bed.number}: ${bed.status}`}</title>
                    {box(bx, by, z + 7, 13, 18, 4, 'model-bed-body')}
                    <polygon className="model-pillow" points={face(bx + 1.5, by + 1.5, z + 7.2, 10, 4)} />
                  </g>
                })}
              </g>
            })}
          </g>
        })}
      </g>
    })}
  </svg>
}
