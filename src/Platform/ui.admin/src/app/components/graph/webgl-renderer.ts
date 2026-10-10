import type { AnnotationTypeCode } from '@/utils/annotation-key'
import type { GraphData } from './graph-model'

// WebGL 2 renderer for the bulk of the graph: links (1 px lines), arrowheads and nodes (instanced, antialiased
// circles). Canvas 2D needed 200-400 ms per frame for 16,000 nodes and 30,000 links, below the 30 fps the spec
// requires (section 12), so GraphCanvas draws these here and keeps labels and highlights on a 2D overlay.

export interface RenderOptions {
  transform: { k: number; x: number; y: number }
  /** CSS pixels. */
  width: number
  height: number
  ratio: number
  arrows: boolean
}

export interface RenderStyle {
  colors: Record<AnnotationTypeCode, string>
  link: string
}

const nodeVertex = `#version 300 es
in vec2 a_corner;
in vec2 a_position;
in float a_radius;
in float a_code;
in float a_alpha;
uniform vec2 u_viewport;
uniform vec3 u_transform;
uniform vec3 u_colors[7];
out vec2 v_local;
out float v_radius;
out vec4 v_color;
void main() {
  float radius = max(a_radius * u_transform.x, 1.5);
  vec2 local = a_corner * (radius + 1.0);
  vec2 screen = a_position * u_transform.x + u_transform.yz + local;
  gl_Position = vec4(screen.x / u_viewport.x * 2.0 - 1.0, 1.0 - screen.y / u_viewport.y * 2.0, 0.0, 1.0);
  v_local = local;
  v_radius = radius;
  v_color = vec4(u_colors[int(a_code)], a_alpha);
}`

const nodeFragment = `#version 300 es
precision highp float;
in vec2 v_local;
in float v_radius;
in vec4 v_color;
out vec4 color;
void main() {
  float coverage = clamp(v_radius - length(v_local) + 0.5, 0.0, 1.0) * v_color.a;
  if (coverage <= 0.0) discard;
  color = vec4(v_color.rgb * coverage, coverage);
}`

const lineVertex = `#version 300 es
in vec2 a_position;
uniform vec2 u_viewport;
uniform vec3 u_transform;
void main() {
  vec2 screen = a_position * u_transform.x + u_transform.yz;
  gl_Position = vec4(screen.x / u_viewport.x * 2.0 - 1.0, 1.0 - screen.y / u_viewport.y * 2.0, 0.0, 1.0);
}`

const arrowVertex = `#version 300 es
in vec2 a_corner;
in vec4 a_arrow;
uniform vec2 u_viewport;
uniform vec3 u_transform;
uniform float u_size;
void main() {
  vec2 direction = a_arrow.zw;
  vec2 normal = vec2(-direction.y, direction.x);
  vec2 screen = a_arrow.xy * u_transform.x + u_transform.yz + (direction * a_corner.x + normal * a_corner.y) * u_size;
  gl_Position = vec4(screen.x / u_viewport.x * 2.0 - 1.0, 1.0 - screen.y / u_viewport.y * 2.0, 0.0, 1.0);
}`

const flatFragment = `#version 300 es
precision highp float;
uniform vec4 u_color;
out vec4 color;
void main() {
  color = u_color;
}`

const codes: AnnotationTypeCode[] = ['rst', 'unt', 'sbt', 'usg', 'cnt', 'exe', 'uxe']

let probe: CanvasRenderingContext2D | null | undefined
const parsed = new Map<string, [number, number, number]>()

/** Any CSS color (hex, rgb, oklch…) as 0..1 RGB, resolved by the browser. */
export function toRgb(color: string): [number, number, number] {
  let rgb = parsed.get(color)
  if (!rgb) {
    probe ??= document.createElement('canvas').getContext('2d', { willReadFrequently: true })
    if (!probe) {
      return [0.5, 0.5, 0.5]
    }
    probe.clearRect(0, 0, 1, 1)
    probe.fillStyle = '#888'
    probe.fillStyle = color
    probe.fillRect(0, 0, 1, 1)
    const [r, g, b] = probe.getImageData(0, 0, 1, 1).data
    rgb = [r! / 255, g! / 255, b! / 255]
    parsed.set(color, rgb)
  }
  return rgb
}

function compile(gl: WebGL2RenderingContext, vertex: string, fragment: string): WebGLProgram {
  const program = gl.createProgram()
  for (const [type, source] of [
    [gl.VERTEX_SHADER, vertex],
    [gl.FRAGMENT_SHADER, fragment],
  ] as const) {
    const shader = gl.createShader(type)!
    gl.shaderSource(shader, source)
    gl.compileShader(shader)
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
      throw new Error(gl.getShaderInfoLog(shader) ?? 'Shader compilation failed')
    }
    gl.attachShader(program, shader)
  }
  gl.linkProgram(program)
  if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
    throw new Error(gl.getProgramInfoLog(program) ?? 'Program link failed')
  }
  return program
}

export class GraphGlRenderer {
  private readonly gl: WebGL2RenderingContext
  private readonly nodeProgram: WebGLProgram
  private readonly lineProgram: WebGLProgram
  private readonly arrowProgram: WebGLProgram
  private readonly nodeVao: WebGLVertexArrayObject
  private readonly lineVao: WebGLVertexArrayObject
  private readonly arrowVao: WebGLVertexArrayObject
  private readonly buffers: Record<
    'corner' | 'triangle' | 'instancePosition' | 'radius' | 'code' | 'alpha' | 'position' | 'links' | 'arrows',
    WebGLBuffer
  >
  private graph: GraphData | undefined
  /** Draw order of the node instances: dimmed nodes first, so normal ones are drawn over them. */
  private order = new Uint32Array(0)
  private instancePositions = new Float32Array(0)
  private arrows = new Float32Array(0)
  private positions: Float32Array | undefined
  private linkCount = 0
  private style: { colors: Float32Array; link: [number, number, number] } | undefined

  /** Undefined when the browser has no WebGL 2 (GraphCanvas then draws everything in 2D). */
  static create(canvas: HTMLCanvasElement): GraphGlRenderer | undefined {
    try {
      const gl = canvas.getContext('webgl2', { antialias: true, premultipliedAlpha: true, alpha: true })
      return gl ? new GraphGlRenderer(gl) : undefined
    } catch {
      return undefined
    }
  }

  private constructor(gl: WebGL2RenderingContext) {
    this.gl = gl
    this.nodeProgram = compile(gl, nodeVertex, nodeFragment)
    this.lineProgram = compile(gl, lineVertex, flatFragment)
    this.arrowProgram = compile(gl, arrowVertex, flatFragment)
    const buffer = () => gl.createBuffer()
    this.buffers = {
      corner: buffer(),
      triangle: buffer(),
      instancePosition: buffer(),
      radius: buffer(),
      code: buffer(),
      alpha: buffer(),
      position: buffer(),
      links: buffer(),
      arrows: buffer(),
    }
    gl.bindBuffer(gl.ARRAY_BUFFER, this.buffers.corner)
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]), gl.STATIC_DRAW)
    gl.bindBuffer(gl.ARRAY_BUFFER, this.buffers.triangle)
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([0, 0, -1, 0.45, -1, -0.45]), gl.STATIC_DRAW)

    this.nodeVao = gl.createVertexArray()
    gl.bindVertexArray(this.nodeVao)
    this.attribute(this.nodeProgram, 'a_corner', this.buffers.corner, 2, 0)
    this.attribute(this.nodeProgram, 'a_position', this.buffers.instancePosition, 2, 1)
    this.attribute(this.nodeProgram, 'a_radius', this.buffers.radius, 1, 1)
    this.attribute(this.nodeProgram, 'a_code', this.buffers.code, 1, 1)
    this.attribute(this.nodeProgram, 'a_alpha', this.buffers.alpha, 1, 1)

    this.lineVao = gl.createVertexArray()
    gl.bindVertexArray(this.lineVao)
    this.attribute(this.lineProgram, 'a_position', this.buffers.position, 2, 0)
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, this.buffers.links)

    this.arrowVao = gl.createVertexArray()
    gl.bindVertexArray(this.arrowVao)
    this.attribute(this.arrowProgram, 'a_corner', this.buffers.triangle, 2, 0)
    this.attribute(this.arrowProgram, 'a_arrow', this.buffers.arrows, 4, 1)
    gl.bindVertexArray(null)
  }

  private attribute(program: WebGLProgram, name: string, buffer: WebGLBuffer, size: number, divisor: number) {
    const gl = this.gl
    const location = gl.getAttribLocation(program, name)
    if (location < 0) {
      return
    }
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer)
    gl.enableVertexAttribArray(location)
    gl.vertexAttribPointer(location, size, gl.FLOAT, false, 0, 0)
    gl.vertexAttribDivisor(location, divisor)
  }

  /** Uploads the static data of a graph and the draw order for the dimmed set. */
  setGraph(graph: GraphData, dimmed: ReadonlySet<number>): void {
    const gl = this.gl
    const count = graph.nodes.length
    this.graph = graph
    this.order = Uint32Array.from([
      ...[...dimmed].filter((index) => index < count),
      ...graph.nodes.map((_, index) => index).filter((index) => !dimmed.has(index)),
    ])
    const radius = new Float32Array(count)
    const code = new Float32Array(count)
    const alpha = new Float32Array(count)
    this.order.forEach((index, slot) => {
      const node = graph.nodes[index]!
      radius[slot] = node.radius
      code[slot] = codes.indexOf(node.code)
      alpha[slot] = dimmed.has(index) ? 0.15 : 1
    })
    gl.bindBuffer(gl.ARRAY_BUFFER, this.buffers.radius)
    gl.bufferData(gl.ARRAY_BUFFER, radius, gl.STATIC_DRAW)
    gl.bindBuffer(gl.ARRAY_BUFFER, this.buffers.code)
    gl.bufferData(gl.ARRAY_BUFFER, code, gl.STATIC_DRAW)
    gl.bindBuffer(gl.ARRAY_BUFFER, this.buffers.alpha)
    gl.bufferData(gl.ARRAY_BUFFER, alpha, gl.STATIC_DRAW)

    const links = new Uint32Array(graph.links.length * 2)
    graph.links.forEach(([source, target], index) => {
      links[index * 2] = source
      links[index * 2 + 1] = target
    })
    this.linkCount = graph.links.length
    gl.bindVertexArray(this.lineVao)
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, this.buffers.links)
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, links, gl.STATIC_DRAW)
    gl.bindVertexArray(null)

    this.instancePositions = new Float32Array(count * 2)
    this.arrows = new Float32Array(graph.links.length * 4)
    this.positions = undefined
  }

  setStyle(style: RenderStyle): void {
    const colors = new Float32Array(codes.length * 3)
    codes.forEach((code, index) => colors.set(toRgb(style.colors[code]), index * 3))
    this.style = { colors, link: toRgb(style.link) }
  }

  setPositions(positions: Float32Array): void {
    const gl = this.gl
    this.positions = positions
    gl.bindBuffer(gl.ARRAY_BUFFER, this.buffers.position)
    gl.bufferData(gl.ARRAY_BUFFER, positions, gl.DYNAMIC_DRAW)
    const order = this.order
    const instances = this.instancePositions
    for (let slot = 0; slot < order.length; slot++) {
      const index = order[slot]!
      instances[slot * 2] = positions[index * 2]!
      instances[slot * 2 + 1] = positions[index * 2 + 1]!
    }
    gl.bindBuffer(gl.ARRAY_BUFFER, this.buffers.instancePosition)
    gl.bufferData(gl.ARRAY_BUFFER, instances, gl.DYNAMIC_DRAW)
  }

  render(options: RenderOptions): void {
    const { gl, graph, positions, style } = this
    const { k } = options.transform
    gl.viewport(0, 0, gl.drawingBufferWidth, gl.drawingBufferHeight)
    gl.clearColor(0, 0, 0, 0)
    gl.clear(gl.COLOR_BUFFER_BIT)
    if (!graph || !positions || !style) {
      return
    }
    gl.enable(gl.BLEND)
    gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA)
    const [lr, lg, lb] = style.link
    const linkAlpha = 0.35

    if (this.linkCount) {
      gl.useProgram(this.lineProgram)
      this.view(this.lineProgram, options)
      gl.uniform4f(
        gl.getUniformLocation(this.lineProgram, 'u_color'),
        lr * linkAlpha,
        lg * linkAlpha,
        lb * linkAlpha,
        linkAlpha,
      )
      gl.bindVertexArray(this.lineVao)
      gl.drawElements(gl.LINES, this.linkCount * 2, gl.UNSIGNED_INT, 0)
    }

    if (options.arrows && this.linkCount) {
      const arrows = this.arrows
      graph.links.forEach(([source, target], index) => {
        const dx = positions[target * 2]! - positions[source * 2]!
        const dy = positions[target * 2 + 1]! - positions[source * 2 + 1]!
        const length = Math.hypot(dx, dy) || 1
        const offset = graph.nodes[target]!.radius + 1 / k
        arrows[index * 4] = positions[target * 2]! - (dx / length) * offset
        arrows[index * 4 + 1] = positions[target * 2 + 1]! - (dy / length) * offset
        arrows[index * 4 + 2] = dx / length
        arrows[index * 4 + 3] = dy / length
      })
      gl.bindBuffer(gl.ARRAY_BUFFER, this.buffers.arrows)
      gl.bufferData(gl.ARRAY_BUFFER, arrows, gl.DYNAMIC_DRAW)
      gl.useProgram(this.arrowProgram)
      this.view(this.arrowProgram, options)
      gl.uniform1f(gl.getUniformLocation(this.arrowProgram, 'u_size'), 5 * Math.sqrt(k))
      gl.uniform4f(
        gl.getUniformLocation(this.arrowProgram, 'u_color'),
        lr * linkAlpha,
        lg * linkAlpha,
        lb * linkAlpha,
        linkAlpha,
      )
      gl.bindVertexArray(this.arrowVao)
      gl.drawArraysInstanced(gl.TRIANGLES, 0, 3, this.linkCount)
    }

    gl.useProgram(this.nodeProgram)
    this.view(this.nodeProgram, options)
    gl.uniform3fv(gl.getUniformLocation(this.nodeProgram, 'u_colors'), style.colors)
    gl.bindVertexArray(this.nodeVao)
    gl.drawArraysInstanced(gl.TRIANGLE_STRIP, 0, 4, this.order.length)
    gl.bindVertexArray(null)
  }

  private view(program: WebGLProgram, options: RenderOptions) {
    const gl = this.gl
    const { k, x, y } = options.transform
    gl.uniform2f(gl.getUniformLocation(program, 'u_viewport'), options.width, options.height)
    gl.uniform3f(gl.getUniformLocation(program, 'u_transform'), k, x, y)
  }

  dispose(): void {
    const gl = this.gl
    Object.values(this.buffers).forEach((buffer) => gl.deleteBuffer(buffer))
    ;[this.nodeVao, this.lineVao, this.arrowVao].forEach((vao) => gl.deleteVertexArray(vao))
    ;[this.nodeProgram, this.lineProgram, this.arrowProgram].forEach((program) => gl.deleteProgram(program))
  }
}
