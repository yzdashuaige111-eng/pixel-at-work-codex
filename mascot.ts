// A mint pixel octopus with a speech-bubble badge. Original artwork, not an OpenAI logo.
// Kept at the upstream 12 x 10 footprint so every prop, mood and holiday hat still fits.
const BODY = [
  '..LOOOOOOL..',
  '.LOOOOOOOOL.',
  '.OOOOOOOOOO.',
  '.OOOOOOOOOO.',
  'OOOOOOOOOOOO',
  'OOOOOOOOOOOO',
  '.OOOOOOOOOO.',
  '..OOOOOOOO..',
]
const LEGS_A = ['O.O.O..O.O.O', 'O.O.O..O.O.O']
const LEGS_B = ['.O.O.OO.O.O.', '.O.O.OO.O.O.']
const BODY_PAL: Pal = { O: C.o, L: C.ol }
type Mood = 'awake' | 'walk' | 'asleep' | 'reading' | 'typing' | 'hop' | 'panic'

function critter(x: number, y: number, mood: Mood = 'awake'): string {
  let s = sprite(BODY, BODY_PAL, x, y)
  const legs = (rows: string[]) => sprite(rows, BODY_PAL, x, y + 8)
  s += mood === 'walk' || mood === 'panic'
    ? frames([legs(LEGS_A), legs(LEGS_B)], mood === 'panic' ? 0.25 : 0.5)
    : legs(LEGS_A)
  if (mood !== 'panic') {
    s += rect(x + 4, y + 5, 5, 2, '#f7faf8') + rect(x + 5, y + 7, 1, 1, '#f7faf8')
    s += rect(x + 5, y + 5, 1, 1, C.o) + rect(x + 7, y + 5, 1, 1, C.o)
  }
  const open = rect(x + 3, y + 2, 1, 2, C.eye) + rect(x + 8, y + 2, 1, 2, C.eye)
  const shut = rect(x + 2, y + 3, 2, 1, C.eye) + rect(x + 8, y + 3, 2, 1, C.eye)
  if (mood === 'asleep') s += shut
  else if (mood === 'panic') {
    s += rect(x + 2, y + 1, 2, 2, C.white) + rect(x + 3, y + 2, 1, 1, C.eye)
    s += rect(x + 8, y + 1, 2, 2, C.white) + rect(x + 8, y + 2, 1, 1, C.eye)
    s += rect(x + 5, y + 4, 2, 2, C.eye)
  } else if (mood === 'reading') {
    s += path(rect(3, 2, 1, 2, C.eye) + rect(8, 2, 1, 2, C.eye), [[x,y],[x+1,y],[x+2,y],[x+1,y]], 2.4)
  } else s += frames([open,open,open,open,open,open,open,shut], 3.2)
  if (mood === 'typing') s += frames([rect(x+12,y+4,2,1,C.o),rect(x+12,y+5,2,1,C.o)], 0.3)
  s += hat(x,y)
  return mood === 'hop'
    ? path(s.replace(/x="(\d+)"/g, (_,v) => 'x="'+(Number(v)-x)+'"')
      .replace(/y="(\d+)"/g, (_,v) => 'y="'+(Number(v)-y)+'"'),
      [[x,y],[x,y-2],[x,y-3],[x,y-2],[x,y],[x,y]], 0.9)
    : s
}

function scChatThink(): string {
  let s = critter(26,14)
  const bubble = rect(47,2,23,11,'#f7faf8') + rect(46,3,25,9,'#f7faf8')
    + rect(48,13,3,2,'#f7faf8') + rect(47,15,2,1,'#f7faf8')
  s += bubble
  s += rect(49,4,9,1,C.o) + rect(49,6,15,1,'#a7e2d1')
  s += frames([0,1,2,3].map(f => {
    let p = f>0 ? rect(49,8,5+f*3,1,'#a7e2d1') : ''
    for(let i=0;i<3;i++) p += rect(61+i*3,10-(i===f%3?1:0),1,1,C.eye)
    return p
  }), 1.2)
  return s
}
