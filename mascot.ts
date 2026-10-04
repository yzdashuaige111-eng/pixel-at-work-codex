// Original conversation robot. New head, screen, torso, articulated arms and two feet.
// Scene props remain MIT adaptations; this character does not use an OpenAI logo.
type Mood = 'awake' | 'walk' | 'asleep' | 'reading' | 'typing' | 'hop' | 'panic'
const ROBOT = { shell:'#f7faf8', edge:'#aebfb8', screen:'#152e28', lamp:'#10a37f', glow:'#a7e2d1' }

function critter(x: number, y: number, mood: Mood = 'awake'): string {
  const r = ROBOT
  let s = rect(5,-2,1,2,r.edge) + rect(5,-3,2,1,mood==='panic'?C.amber:r.lamp)
  // The stepped silhouette is a round head, separate from the smaller body.
  s += rect(3,0,6,1,r.edge) + rect(2,1,8,4,r.edge) + rect(1,2,10,2,r.edge)
  s += rect(3,1,6,3,r.shell) + rect(2,2,8,2,r.shell)
  s += rect(3,1,6,3,r.screen)
  const open = rect(4,2,1,2,r.glow) + rect(7,2,1,2,r.glow)
  const shut = rect(4,3,2,1,r.edge) + rect(7,3,2,1,r.edge)
  if(mood==='asleep') s += shut
  else if(mood==='panic') s += rect(5,1,2,2,C.amber) + rect(5,3,2,1,r.shell)
  else if(mood==='reading') s += path(rect(4,2,1,1,r.glow)+rect(7,2,1,1,r.glow),[[0,0],[0,1],[1,1],[0,1]],1.8)
  else s += frames([open,open,open,shut,open],2.8)
  s += rect(5,5,2,1,r.screen) + rect(4,6,4,2,r.edge) + rect(4,6,3,1,r.shell)
  s += rect(5,6,2,1,mood==='asleep'?r.edge:r.lamp) + rect(5,7,1,1,r.lamp)
  s += rect(2,5,1,1,r.screen) + rect(1,6,3,1,r.shell) + rect(9,5,1,1,r.screen)
  if(mood==='typing') s += frames([rect(9,6,4,1,r.shell),rect(9,5,3,1,r.shell)+rect(11,6,2,1,r.edge)],0.4)
  else s += rect(8,6,3,1,r.shell)
  const feet = (step: boolean) => rect(4,8,1,1,r.edge)+rect(7,8,1,1,r.edge)
    + rect(step?4:3,9,3,1,r.screen)+rect(step?6:7,9,3,1,r.screen)
  s += mood==='walk'||mood==='panic' ? frames([feet(false),feet(true)],mood==='panic'?0.3:0.6) : feet(false)
  s += hat(0,0)
  return mood==='hop' ? path(s,[[x,y],[x,y-1],[x,y-2],[x,y],[x,y]],0.8) : shift(x,y,s)
}

function scChatThink(): string {
  let s = critter(26,14)
  s += rect(47,2,23,11,ROBOT.shell) + rect(46,3,25,9,ROBOT.shell)
    + rect(48,13,3,2,ROBOT.shell) + rect(47,15,2,1,ROBOT.shell)
  s += rect(49,4,9,1,ROBOT.lamp) + rect(49,6,15,1,ROBOT.glow)
  s += frames([0,1,2,3].map(f => {
    let p = f>0 ? rect(49,8,5+f*3,1,ROBOT.glow) : ''
    for(let i=0;i<3;i++) p += rect(61+i*3,10-(i===f%3?1:0),1,1,ROBOT.screen)
    return p
  }),1.2)
  return s
}
