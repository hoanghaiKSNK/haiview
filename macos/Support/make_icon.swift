import AppKit
let out = CommandLine.arguments[1]
func render(_ px: Int) -> Data {
    let s = CGFloat(px)
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let inset = s * 0.1
    let bg = NSBezierPath(roundedRect: NSRect(x: inset, y: inset, width: s - 2*inset, height: s - 2*inset), xRadius: s*0.18, yRadius: s*0.18)
    NSGradient(starting: NSColor(red: 0.10, green: 0.32, blue: 0.62, alpha: 1), ending: NSColor(red: 0.05, green: 0.16, blue: 0.36, alpha: 1))!.draw(in: bg, angle: -90)
    // 2 trên, 1 dưới
    let w = s*0.30, h = w*9/16, g = s*0.04
    let top = s*0.56, bottom = top - h - g*1.6
    let rects = [NSRect(x: s/2 - w - g/2, y: top, width: w, height: h),
                 NSRect(x: s/2 + g/2, y: top, width: w, height: h),
                 NSRect(x: s/2 - w/2, y: bottom, width: w, height: h)]
    for r in rects {
        NSColor.white.setFill(); NSBezierPath(roundedRect: r, xRadius: s*0.012, yRadius: s*0.012).fill()
        NSColor(red: 0.10, green: 0.32, blue: 0.62, alpha: 0.9).setFill()
        NSBezierPath(rect: NSRect(x: r.minX + r.width*0.1, y: r.maxY - r.height*0.3, width: r.width*0.6, height: r.height*0.12)).fill()
        NSColor(white: 0.75, alpha: 1).setFill()
        for k in 0..<2 { NSBezierPath(rect: NSRect(x: r.minX + r.width*0.1, y: r.minY + r.height*(0.22 + 0.2*CGFloat(k)), width: r.width*0.75, height: r.height*0.07)).fill() }
    }
    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}
try? FileManager.default.createDirectory(atPath: out, withIntermediateDirectories: true)
for (name, px) in [("16x16",16),("16x16@2x",32),("32x32",32),("32x32@2x",64),("128x128",128),("128x128@2x",256),("256x256",256),("256x256@2x",512),("512x512",512),("512x512@2x",1024)] {
    try! render(px).write(to: URL(fileURLWithPath: "\(out)/icon_\(name).png"))
}
