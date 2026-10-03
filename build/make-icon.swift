// CC-Merger for Mac — app icon.
//
// The Windows CCMerger had no icon of its own (only the Windows logo for its
// taskbar), so this one is new, in the style of the other GramzeSweatshop Mac
// tools: three small package boxes on the left, arrows flowing from them into
// one big box — many packages merged into one.
//
// Usage: swift build/make-icon.swift <out.png>   (1024x1024 PNG)
// build/make-icon.sh turns it into build/AppIcon.icns.

import AppKit

let size = 1024.0
let out = CommandLine.arguments[1]
let cs = CGColorSpace(name: CGColorSpace.sRGB)!
let ctx = CGContext(data: nil, width: Int(size), height: Int(size), bitsPerComponent: 8, bytesPerRow: 0,
                    space: cs, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!

func rgb(_ r: Double, _ g: Double, _ b: Double, _ a: Double = 1) -> CGColor { CGColor(srgbRed: r / 255, green: g / 255, blue: b / 255, alpha: a) }

// macOS icon grid: 824-point rounded square centred on the 1024 canvas.
let tile = CGRect(x: 100, y: 100, width: 824, height: 824)
let tilePath = CGPath(roundedRect: tile, cornerWidth: 185, cornerHeight: 185, transform: nil)

ctx.saveGState()
ctx.setShadow(offset: CGSize(width: 0, height: -12), blur: 28, color: rgb(0, 0, 0, 0.30))
ctx.addPath(tilePath); ctx.setFillColor(rgb(255, 255, 255)); ctx.fillPath()
ctx.restoreGState()

ctx.saveGState()
ctx.addPath(tilePath); ctx.clip()
let sky = CGGradient(colorsSpace: cs, colors: [rgb(214, 234, 250), rgb(246, 250, 255)] as CFArray, locations: [0, 1])!
ctx.drawLinearGradient(sky, start: CGPoint(x: 0, y: 100), end: CGPoint(x: 0, y: 924), options: [])
ctx.restoreGState()

// An isometric cardboard box centred at c with edge length s.
func box(_ c: CGPoint, _ s: Double, top: CGColor, left: CGColor, right: CGColor, tape: CGColor) {
    let w = s * 0.866, h = s * 0.5
    let topC = CGPoint(x: c.x, y: c.y + s * 0.5)
    // top face
    let t0 = CGPoint(x: topC.x, y: topC.y + h), t1 = CGPoint(x: topC.x + w, y: topC.y)
    let t2 = CGPoint(x: topC.x, y: topC.y - h), t3 = CGPoint(x: topC.x - w, y: topC.y)
    let down = CGPoint(x: 0, y: -s)
    func add(_ p: CGPoint, _ q: CGPoint) -> CGPoint { CGPoint(x: p.x + q.x, y: p.y + q.y) }
    ctx.saveGState()
    ctx.setShadow(offset: CGSize(width: 0, height: -10), blur: 18, color: rgb(0, 0, 0, 0.25))
    // left face
    ctx.move(to: t3); ctx.addLine(to: t2); ctx.addLine(to: add(t2, down)); ctx.addLine(to: add(t3, down)); ctx.closePath()
    ctx.setFillColor(left); ctx.fillPath()
    ctx.restoreGState()
    // right face
    ctx.move(to: t2); ctx.addLine(to: t1); ctx.addLine(to: add(t1, down)); ctx.addLine(to: add(t2, down)); ctx.closePath()
    ctx.setFillColor(right); ctx.fillPath()
    // top face
    ctx.move(to: t0); ctx.addLine(to: t1); ctx.addLine(to: t2); ctx.addLine(to: t3); ctx.closePath()
    ctx.setFillColor(top); ctx.fillPath()
    // tape across the top and down the front edge
    ctx.setStrokeColor(tape); ctx.setLineWidth(s * 0.16); ctx.setLineCap(.butt)
    let m0 = CGPoint(x: (t0.x + t3.x) / 2, y: (t0.y + t3.y) / 2), m1 = CGPoint(x: (t1.x + t2.x) / 2, y: (t1.y + t2.y) / 2)
    ctx.move(to: m0); ctx.addLine(to: m1); ctx.addLine(to: add(m1, CGPoint(x: 0, y: -s * 0.35))); ctx.strokePath()
    // edges
    ctx.setStrokeColor(rgb(120, 80, 40, 0.55)); ctx.setLineWidth(max(2, s * 0.025)); ctx.setLineJoin(.round)
    ctx.move(to: t0); ctx.addLine(to: t1); ctx.addLine(to: t2); ctx.addLine(to: t3); ctx.closePath()
    ctx.move(to: t3); ctx.addLine(to: add(t3, down)); ctx.addLine(to: add(t2, down)); ctx.addLine(to: add(t1, down)); ctx.addLine(to: t1)
    ctx.move(to: t2); ctx.addLine(to: add(t2, down))
    ctx.strokePath()
}

let cardTop = rgb(232, 196, 140), cardLeft = rgb(200, 150, 92), cardRight = rgb(176, 126, 72), tape = rgb(242, 224, 186)

// Three small boxes, top to bottom on the left.
let small: [CGPoint] = [CGPoint(x: 280, y: 690), CGPoint(x: 280, y: 480), CGPoint(x: 280, y: 270)]

// Arrows from each small box to the big box (drawn first, under the boxes).
let target = CGPoint(x: 530, y: 440)
ctx.setStrokeColor(rgb(36, 112, 214)); ctx.setFillColor(rgb(36, 112, 214))
ctx.setLineWidth(30); ctx.setLineCap(.round)
for p in small {
    let start = CGPoint(x: p.x + 95, y: p.y + 10)
    let end = CGPoint(x: target.x - 10, y: target.y + 15 + (p.y - 480) * 0.25)
    let ctrl = CGPoint(x: (start.x + end.x) / 2, y: start.y)
    ctx.move(to: start); ctx.addQuadCurve(to: end, control: ctrl); ctx.strokePath()
    // arrowhead along the final tangent
    let dx = end.x - ctrl.x, dy = end.y - ctrl.y, len = (dx * dx + dy * dy).squareRoot()
    let ux = dx / len, uy = dy / len
    let head = 56.0, half = 38.0
    let tip = CGPoint(x: end.x + ux * head * 0.6, y: end.y + uy * head * 0.6)
    ctx.move(to: tip)
    ctx.addLine(to: CGPoint(x: end.x - ux * head * 0.4 - uy * half, y: end.y - uy * head * 0.4 + ux * half))
    ctx.addLine(to: CGPoint(x: end.x - ux * head * 0.4 + uy * half, y: end.y - uy * head * 0.4 - ux * half))
    ctx.closePath(); ctx.fillPath()
}

for p in small { box(p, 92, top: cardTop, left: cardLeft, right: cardRight, tape: tape) }

// The big merged box.
box(CGPoint(x: 710, y: 400), 185, top: cardTop, left: cardLeft, right: cardRight, tape: tape)

let rep = NSBitmapImageRep(cgImage: ctx.makeImage()!)
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: out))
