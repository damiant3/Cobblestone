# sampler-table-oracle.py -- Diffusion Forge's sampler table, read from its own
# source, printed as the Codex list literal codex/test/apps/diffusion-sampler-table
# grades apps/diffusion/Txt2Img's dispatch against.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/sampler-table-oracle.py
#
# The k-diffusion samplers are modules/sd_samplers_kdiffusion.py's
# samplers_k_diffusion, parsed with ast (the list names a function object, so
# it is not evaluated): each label with its 'scheduler' option and its
# 'discard_next_to_last_sigma' flag. The timestep samplers are
# sd_samplers_timesteps.py's samplers_timesteps, LCM is sd_samplers_lcm.py's
# samplers_lcm and DDPM is modules_forge/alter_samplers.py's samplers_data_alter;
# none of them carries a schedule option. A schedule option is printed as the
# label sd_schedulers.py's Scheduler table gives that name, "Uniform" for no
# option (Forge then calls the predictor's get_sigmas); the scheduler labels
# are printed in that table's order. One line per sampler, "label|schedule|kind|discard",
# kind k (k-diffusion), t (timestep), l (LCM) or a (alter).
import ast, os

WEBUI = r'D:\AI\DiffusionForge\webui'

def tree(rel):
    return ast.parse(open(os.path.join(WEBUI, rel), encoding='utf-8').read())

def assigned(t, name):
    for node in ast.walk(t):
        if isinstance(node, ast.Assign) and any(isinstance(x, ast.Name) and x.id == name for x in node.targets):
            return node.value
    raise SystemExit('REFUSE: no %s' % name)

def options(d):
    return {k.value: (v.value if isinstance(v, ast.Constant) else None) for k, v in zip(d.keys, d.values)} if isinstance(d, ast.Dict) else {}

sched = {}
sched_labels = []
for call in assigned(tree('modules/sd_schedulers.py'), 'schedulers').elts:
    name, label = call.args[0].value, call.args[1].value
    sched[name] = label
    sched_labels.append(label)

rows = []
for e in assigned(tree('modules/sd_samplers_kdiffusion.py'), 'samplers_k_diffusion').elts:
    o = options(e.elts[3])
    rows.append((e.elts[0].value, sched[o['scheduler']] if 'scheduler' in o else 'Uniform', 'k', 1 if o.get('discard_next_to_last_sigma') else 0))
for e in assigned(tree('modules/sd_samplers_timesteps.py'), 'samplers_timesteps').elts:
    rows.append((e.elts[0].value, 'none', 't', 0))
for e in assigned(tree('modules/sd_samplers_lcm.py'), 'samplers_lcm').elts:
    rows.append((e.elts[0].value, 'LCM', 'l', 0))
for call in assigned(tree('modules_forge/alter_samplers.py'), 'samplers_data_alter').elts:
    rows.append((call.args[0].value, 'Uniform', 'a', 0))

print('  forge-schedulers : List Text = [%s]' % ', '.join('"%s"' % s for s in sched_labels))
print('  forge-samplers : List Text = [%s]' % ', '.join('"%s|%s|%s|%d"' % r for r in rows))
